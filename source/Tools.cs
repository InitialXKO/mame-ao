using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Data;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Spludlow.MameAO
{
	public class Tools
	{
		private static readonly string[] _SystemOfUnits =
		{
			"Bytes (B)",
			"Kilobytes (KiB)",
			"Megabytes (MiB)",
			"Gigabytes (GiB)",
			"Terabytes (TiB)",
			"Petabytes (PiB)",
			"Exabytes (EiB)"
		};

		private static readonly char[] _HeadingChars = new char[] { ' ', '#', '=', '-' };

		private static readonly SHA1Managed _SHA1Managed = new SHA1Managed();

		public static string DataRowValue(DataRow row, string columnName)
		{
			if (row.IsNull(columnName))
				return null;
			return (string)row[columnName];
		}

		public static void ConsoleRule(int head)
		{
			Console.WriteLine(new String(_HeadingChars[head], Console.WindowWidth - 1));
		}

		public static void ConsoleHeading(int head, string line)
		{
			ConsoleHeading(head, new string[] { line });
		}
		public static void ConsoleHeading(int head, string[] lines)
		{
			ConsoleRule(head);

			char ch = _HeadingChars[head];

			foreach (string line in lines)
			{
				int pad = Console.WindowWidth - 3 - line.Length;
				if (pad < 1)
					pad = 1;
				int odd = pad % 2;
				pad /= 2;

				Console.Write(ch);
				Console.Write(new String(' ', pad));
				Console.Write(line);
				Console.Write(new String(' ', pad + odd));
				Console.Write(ch);
				Console.WriteLine();
			}

			ConsoleRule(head);
		}

		public static void CleanDynamic(dynamic data)
		{
			List<string> deleteList = new List<string>();
			foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(data))
			{
				if (descriptor.GetValue(data) == null)
					deleteList.Add(descriptor.Name);
			}

			foreach (string key in deleteList)
				((JObject)data).Remove(key);
		}

		public static void PopText(DataTable table)
		{
			PopText(TextTable(table));
		}
		public static void PopText(string text)
		{
			string filename = Path.GetTempFileName();
			File.WriteAllText(filename, text, Encoding.UTF8);
			Process.Start("notepad.exe", filename);
			Environment.Exit(0);
		}

		public static string TextTable(DataTable table)
		{
			StringBuilder result = new StringBuilder();

			foreach (DataColumn column in table.Columns)
			{
				if (column.Ordinal != 0)
					result.Append('\t');

				result.Append(column.ColumnName);
			}
			result.AppendLine();

			foreach (DataColumn column in table.Columns)
			{
				if (column.Ordinal != 0)
					result.Append('\t');

				result.Append(column.DataType);
			}
			result.AppendLine();

			foreach (DataRow row in table.Rows)
			{
				foreach (DataColumn column in table.Columns)
				{
					if (column.Ordinal != 0)
						result.Append('\t');

					object value = row[column];

					if (value != null)
						result.Append(Convert.ToString(value));
				}
				result.AppendLine();
			}

			return result.ToString();
		}

		private static List<char> _InvalidFileNameChars = new List<char>(Path.GetInvalidFileNameChars());

		public static string ValidFileName(string name)
		{
			return ValidName(name, _InvalidFileNameChars, "_");
		}

		private static string ValidName(string name, List<char> invalidChars, string replaceBadWith)
		{
			StringBuilder sb = new StringBuilder();

			foreach (char c in name)
			{
				if (invalidChars.Contains(c) == true)
					sb.Append(replaceBadWith);
				else
					sb.Append(c);
			}

			return sb.ToString();
		}

		public static string SHA1HexFile(string filename)
		{
			using (FileStream stream = File.OpenRead(filename))
			{
				byte[] hash = _SHA1Managed.ComputeHash(stream);
				StringBuilder hex = new StringBuilder();
				foreach (byte b in hash)
					hex.Append(b.ToString("x2"));
				return hex.ToString();
			}
		}

		public static void ClearAttributes(string directory)
		{
			foreach (string filename in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
				File.SetAttributes(filename, FileAttributes.Normal);
		}

		public static string PrettyJSON(string json)
		{
			dynamic obj = JsonConvert.DeserializeObject<dynamic>(json);
			return JsonConvert.SerializeObject(obj, Newtonsoft.Json.Formatting.Indented);
		}

		public static DataTable MakeDataTable(string columnNames, string columnTypes)
		{
			string[] names = columnNames.Split(new char[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
			string[] types = columnTypes.Split(new char[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);

			if (names.Length != types.Length)
				throw new ApplicationException("Make Data Table Bad definition.");

			DataTable table = new DataTable();

			List<int> keyColumnIndexes = new List<int>();

			for (int index = 0; index < names.Length; ++index)
			{
				string name = names[index];
				string typeName = "System." + types[index];

				if (typeName.EndsWith("*") == true)
				{
					typeName = typeName.Substring(0, typeName.Length - 1);
					keyColumnIndexes.Add(index);
				}

				table.Columns.Add(name, Type.GetType(typeName, true));
			}

			if (keyColumnIndexes.Count > 0)
			{
				List<DataColumn> keyColumns = new List<DataColumn>();
				foreach (int index in keyColumnIndexes)
					keyColumns.Add(table.Columns[index]);
				table.PrimaryKey = keyColumns.ToArray();
			}

			return table;
		}

		public static string Query(HttpClient client, string url)
		{
			using (HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Get, url))
			{
				Task<HttpResponseMessage> requestTask = client.SendAsync(requestMessage);
				requestTask.Wait();
				HttpResponseMessage responseMessage = requestTask.Result;

				responseMessage.EnsureSuccessStatusCode();

				Task<string> responseMessageTask = responseMessage.Content.ReadAsStringAsync();
				responseMessageTask.Wait();
				string responseBody = responseMessageTask.Result;

				return responseBody;
			}
		}



		public static long Download(string url, string filename, long progressSize, int timeoutMinutes)
		{
			long total = 0;
			byte[] buffer = new byte[64 * 1024];

			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
			request.Method = "GET";

			request.Timeout = timeoutMinutes * (60 * 1000);

			long progress = 0;

			using (WebResponse response = request.GetResponse())
			{
				using (Stream sourceStream = response.GetResponseStream())
				{
					using (FileStream targetStream = new FileStream(filename, FileMode.Create, FileAccess.Write))
					{
						int bytesRead;
						while ((bytesRead = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
						{
							total += bytesRead;
							targetStream.Write(buffer, 0, bytesRead);

							if (progressSize > 0)
							{
								progress += bytesRead;
								if (progress >= progressSize)
								{
									Console.Write(".");
									progress = 0;
								}
							}
						}
					}
				}
			}

			return total;
		}

		public static void LinkFiles(string[][] linkTargetFilenames)
		{
			HashSet<string> linkDirectories = new HashSet<string>();

			StringBuilder batch = new StringBuilder();

			for (int index = 0; index < linkTargetFilenames.Length; ++index)
			{
				string link = linkTargetFilenames[index][0];
				string target = linkTargetFilenames[index][1];

				string linkDirectory = Path.GetDirectoryName(link);
				linkDirectories.Add(linkDirectory);

				//	Escape characters, may be more
				link = link.Replace("%", "%%");

				batch.Append("mklink ");
				batch.Append('\"');
				batch.Append(link);
				batch.Append("\" \"");
				batch.Append(target);
				batch.Append('\"');
				batch.AppendLine();
			}

			foreach (string linkDirectory in linkDirectories)
			{
				if (Directory.Exists(linkDirectory) == false)
					Directory.CreateDirectory(linkDirectory);
			}

			using (TempDirectory tempDir = new TempDirectory())
			{
				string batchFilename = tempDir.Path + @"\link.bat";
				File.WriteAllText(batchFilename, batch.ToString(), new UTF8Encoding(false));

				string input = "chcp 65001" + Environment.NewLine + batchFilename + Environment.NewLine;

				ProcessStartInfo startInfo = new ProcessStartInfo("cmd.exe")
				{
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardInput = true,
				};

				using (Process process = new Process())
				{
					process.StartInfo = startInfo;

					process.Start();

					process.StandardInput.WriteLine(input);
					process.StandardInput.Close();

					process.WaitForExit();
				}
			}
		}

		public static string DataSize(long sizeBytes)
		{
			return DataSize((ulong)sizeBytes);
		}
		public static string DataSize(ulong sizeBytes)
		{
			for (int index = 0; index < _SystemOfUnits.Length; ++index)
			{
				ulong nextUnit = (ulong)Math.Pow(2, (index + 1) * 10);

				if (sizeBytes < nextUnit || nextUnit == 0 || index == (_SystemOfUnits.Length - 1))
				{
					ulong unit = (ulong)Math.Pow(2, index * 10);
					decimal result = (decimal)sizeBytes / (decimal)unit;
					int decimalPlaces = 0;
					if (result <= 9.9M)
						decimalPlaces = 1;
					result = Math.Round(result, decimalPlaces);
					return result.ToString() + " " + _SystemOfUnits[index];
				}
			}

			throw new ApplicationException("Failed to find Data Size: " + sizeBytes.ToString());
		}
	}

	public class TempDirectory : IDisposable
	{
		private readonly string _LockFilePath;
		private readonly string _Path;

		public TempDirectory()
		{
			//			LockFilePath = @"\\?\" + System.IO.Path.GetTempFileName(); //	Long filename support

			_LockFilePath = System.IO.Path.GetTempFileName();
			_Path = _LockFilePath + ".dir";

			Directory.CreateDirectory(this._Path);
		}

		public void Dispose()
		{
			if (Directory.Exists(_Path) == true)
				Directory.Delete(_Path, true);

			if (_LockFilePath != null)
				File.Delete(_LockFilePath);
		}

		public string Path
		{
			get => _Path;
		}

		public override string ToString()
		{
			return _Path;
		}
	}

}
