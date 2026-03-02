using System;
using System.IO;
using System.Text;

public static class Program
{
	public static int Main(string[] args)
	{
		if (args.Length != 3)
		{
			Console.WriteLine("Usage: program <input.png> <append.bin> <output.png>");
			return 1;
		}

		var inputPng = args[0];
		var appendFile = args[1];
		var outputPng = args[2];

		if (!File.Exists(inputPng) || !File.Exists(appendFile))
		{
			Console.WriteLine("Input file not found.");
			return 2;
		}

		using (var input = new FileStream(inputPng, FileMode.Open, FileAccess.Read))
		using (var output = new FileStream(outputPng, FileMode.Create, FileAccess.Write))
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

			// 署名を書き出す
			output.Write(signature, 0, 8);

			// ---- チャンク処理 ----
			while (input.Position < input.Length)
			{
				byte[] lengthBytes = br.ReadBytes(4);
				if (lengthBytes.Length < 4)
				{
					break;
				}

				output.Write(lengthBytes, 0, 4);

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

				output.Write(typeBytes, 0, 4);

				var chunkType = Encoding.ASCII.GetString(typeBytes);

				long remaining = length + 4; // data + CRC

				const int bufferSize = 8192;
				byte[] buffer = new byte[bufferSize];

				while (0 < remaining)
				{
					int toRead = (bufferSize < remaining) ? bufferSize : (int)remaining;
					int read = input.Read(buffer, 0, toRead);

					if (read <= 0)
					{
						break;
					}

					output.Write(buffer, 0, read);
					remaining -= read;
				}

				if (chunkType == "IEND")
				{
					break;
				}
			}

			// ---- 追加データをストリームコピー ----
			using (var append = new FileStream(appendFile, FileMode.Open, FileAccess.Read))
			{
				append.CopyTo(output);
			}
		}

		Console.WriteLine("IEND found and data appended successfully.");
		return 0;
	}
}
