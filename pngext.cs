using System;
using System.IO;
using System.Text;

public static class Program
{
	public static int Main(string[] args)
	{
		if (args.Length != 2)
		{
			Console.WriteLine("Usage: program <input.png> <output.bin>");
			return 1;
		}

		var inputPng = args[0];
		var outputFile = args[1];

		if (!File.Exists(inputPng))
		{
			Console.WriteLine("Input file not found.");
			return 2;
		}

		using (var input = new FileStream(inputPng, FileMode.Open, FileAccess.Read))
		using (var br = new BinaryReader(input))
		{
			// ---- PNG signature ----
			byte[] signature = br.ReadBytes(8);
			if (signature.Length != 8)
			{
				Console.WriteLine("Invalid PNG file.");
				return 3;
			}

			byte[] expected = 
			{
				137, 80, 78, 71, 13, 10, 26, 10, 
			};

			for (int i = 0; i < 8; i ++)
			{
				if (signature[i] != expected[i])
				{
					Console.WriteLine("Not a valid PNG signature.");
					return 4;
				}
			}

			// ---- チャンク走査 ----
			while (input.Position < input.Length)
			{
				byte[] lengthBytes = br.ReadBytes(4);
				if (lengthBytes.Length < 4)
				{
					break;
				}

				uint length =
					(uint)(lengthBytes[0] << 24 | 
					lengthBytes[1] << 16 | 
					lengthBytes[2] << 8 | 
					lengthBytes[3]);

				byte[] typeBytes = br.ReadBytes(4);
				if (typeBytes.Length < 4)
				{
					break;
				}

				var chunkType = Encoding.ASCII.GetString(typeBytes);

				long remaining = length + 4; // data + CRC

				while (0 < remaining)
				{
					long skipped = input.Seek(remaining, SeekOrigin.Current);
					remaining = 0;
				}

				if (chunkType == "IEND")
				{
					break;
				}
			}

			// ---- IEND後にデータがあるか確認 ----
			if (input.Length <= input.Position)
			{
				Console.WriteLine("No extra data after IEND.");
				return 0;
			}

			using (var output = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
			{
				const int bufferSize = 8192;
				byte[] buffer = new byte[bufferSize];

				int read;

				while (0 < (read = input.Read(buffer, 0, bufferSize)))
				{
					output.Write(buffer, 0, read);
				}
			}
		}

		Console.WriteLine("Extra data extracted successfully.");
		return 0;
	}
}
