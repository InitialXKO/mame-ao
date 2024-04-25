﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace Spludlow.MameAO
{
	public class Samples
	{
		public string Version = "";
		public DataSet DataSet = null;

		private readonly string MameSamplesDirectory;

		public Samples()
		{
			MameSamplesDirectory = Path.Combine(Globals.MameDirectory, "samples");
		}

		public void Initialize()
		{
			GitHubRepo repo = Globals.GitHubRepos["MAME_Dats"];

			string url = repo.UrlRaw + "/main/MAME_dat/MAME_Samples.dat";

			Tools.ConsoleHeading(2, new string[] {
				$"Machine Samples",
				url
			});

			string xml = repo.Fetch(url);

			if (xml == null)
				return;

			Version = ParseXML(xml);

			DataSet.Tables["machine"].PrimaryKey = new DataColumn[] { DataSet.Tables["machine"].Columns["name"] };
			DataSet.Tables["rom"].PrimaryKey = new DataColumn[] { DataSet.Tables["rom"].Columns["machine_id"], DataSet.Tables["rom"].Columns["name"] };

			foreach (DataRow row in DataSet.Tables["rom"].Rows)
			{
				if (row.IsNull("sha1") == false)
					Globals.Database._AllSHA1s.Add((string)row["sha1"]);
			}

			Console.WriteLine($"Version:\t{Version}");
		}

		private string ParseXML(string xml)
		{
			XElement document = XElement.Parse(xml);
			DataSet = new DataSet();
			ReadXML.ImportXMLWork(document, DataSet, null, null);

			return GetDataSetVersion(DataSet);
		}

		public string GetDataSetVersion(DataSet dataSet)
		{
			if (dataSet.Tables.Contains("header") == false)
				throw new ApplicationException("No header table");

			DataTable table = dataSet.Tables["header"];

			if (table.Rows.Count != 1)
				throw new ApplicationException("Not one header row");

			return (string)table.Rows[0]["version"];
		}

		public void PlaceSamples(DataRow machineRow)
		{
			if (DataSet == null)
				return;

			string[] sampleNames = Globals.Database.GetMachineSamples(machineRow).Select(row => (string)row["name"]).ToArray();

			if (sampleNames.Length == 0)
				return;

			string machineName = (string)machineRow["name"];

			Tools.ConsoleHeading(2, new string[] {
				$"Machine Samples: {machineName} ({sampleNames.Length})",
			});

			string downloadMachineName = machineName;
			if (machineRow.IsNull("cloneof") == false)
				downloadMachineName = (string)machineRow["cloneof"];

			DataRow sampleMachineRow = DataSet.Tables["machine"].Rows.Find(downloadMachineName);

			if (sampleMachineRow == null)
			{
				Console.WriteLine($"!!! Sample machine not found: {machineName}");
				return;
			}

			//
			// Find required Samples
			//

			long machine_id = (long)sampleMachineRow["machine_id"];

			List<DataRow> sampleRoms = new List<DataRow>();

			foreach (string sampleName in sampleNames)
			{
				DataRow sampleRom = DataSet.Tables["rom"].Rows.Find(new object[] { machine_id, sampleName + ".wav" });

				if (sampleRom == null)
				{
					Console.WriteLine($"!!! Sample not found: {machineName}\t{sampleName}");
					continue;
				}

				if (sampleRom.IsNull("name") || sampleRom.IsNull("sha1"))
					continue;
				sampleRoms.Add(sampleRom);

				Console.WriteLine($"Sample Required:\t{sampleRom["sha1"]}\t{sampleRom["name"]}");
			}

			//
			// Import if required
			//

			bool inStore = true;
			foreach (DataRow sampleRom in sampleRoms)
			{
				string sha1 = (string)sampleRom["sha1"];
				if (Globals.RomHashStore.Exists(sha1) == false)
					inStore = false;
			}

			if (inStore == false)
			{
				ArchiveOrgItem item = Globals.ArchiveOrgItems[ItemType.Support][0];

				ArchiveOrgFile file = item.GetFile($"Samples/{downloadMachineName}");

				string url = item.DownloadLink(file);

				using (TempDirectory tempDir = new TempDirectory())
				{
					string zipFilename = Path.Combine(tempDir.Path, machineName + ".zip");
					Tools.Download(url, zipFilename, 0, 10);

					ZipFile.ExtractToDirectory(zipFilename, tempDir.Path);
					Tools.ClearAttributes(tempDir.Path);

					foreach (string wavFilename in Directory.GetFiles(tempDir.Path, "*.wav"))
					{
						string fileSha1 = Globals.RomHashStore.Hash(wavFilename);
						bool required = Globals.Database._AllSHA1s.Contains(fileSha1);
						bool imported = false;

						if (required == true)
							imported = Globals.RomHashStore.Add(wavFilename);

						Console.WriteLine($"Sample Imported:\t{fileSha1}\t{required}\t{imported}");
					}
				}
			}

			//
			// Place
			//

			List<string[]> wavStoreFilenames = new List<string[]>();

			foreach (DataRow row in sampleRoms)
			{
				string name = (string)row["name"];
				string sha1 = (string)row["sha1"];

				string wavFilename = Path.Combine(MameSamplesDirectory, machineName, name);

				bool fileExists = File.Exists(wavFilename);
				bool storeExists = Globals.RomHashStore.Exists(sha1);

				if (fileExists == false && storeExists == true)
				{
					wavStoreFilenames.Add(new string[] { wavFilename, Globals.RomHashStore.Filename(sha1) });
				}

				Console.WriteLine($"Place Sample:\t{sha1}\t{fileExists}\t{storeExists}\t{wavFilename}");
			}

			if (Globals.LinkingEnabled == true)
			{
				Tools.LinkFiles(wavStoreFilenames.ToArray());
			}
			else
			{
				foreach (string[] wavStoreFilename in wavStoreFilenames)
					File.Copy(wavStoreFilename[1], wavStoreFilename[0], true);
			}
		}
	}
}
