using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using QRCoder;

namespace QRTSender
{
	public class Frame
	{
		public byte Type;
		public uint TransferId;
		public ushort Index;
		public ushort Count;
		public byte[] Payload;
	}

	public class Metadata
	{
		public string Name;
		public long Size;
		public int FrameCount;
		public string Compression;
		public string Sha256;
	}

	public static class Crc32
	{
		private static readonly uint[] Table = CreateTable();

		private static uint[] CreateTable()
		{
			var table = new uint[256];

			for (uint i = 0; i < 256; i ++)
			{
				var crc = i;

				for (var j = 0; j < 8; j ++)
				{
					if ((crc & 1) != 0)
					{
						crc = 0xEDB88320U ^ (crc >> 1);
					}
					else
					{
						crc >>= 1;
					}
				}

				table[i] = crc;
			}

			return table;
		}

		public static uint Compute(byte[] data)
		{
			var crc = 0xFFFFFFFFU;

			for (var i = 0; i < data.Length; i ++)
			{
				var index = (byte)((crc ^ data[i]) & 0xFF);
				crc = Table[index] ^ (crc >> 8);
			}

			return crc ^ 0xFFFFFFFFU;
		}
	}

	public static class FrameEncoder
	{
		public static byte[] SerializeFrame(Frame frame)
		{
			byte[] body;

			using (var ms = new MemoryStream())
			using (var bw = new BinaryWriter(ms))
			{
				bw.Write(frame.Type);
				bw.Write(frame.TransferId);
				bw.Write(frame.Index);
				bw.Write(frame.Count);
				bw.Write((ushort)frame.Payload.Length);
				bw.Write(frame.Payload);

				bw.Flush();

				body = ms.ToArray();
			}

			var crc = Crc32.Compute(body);

			using (var ms = new MemoryStream())
			using (var bw = new BinaryWriter(ms))
			{
				bw.Write(body);
				bw.Write(crc);

				bw.Flush();

				return ms.ToArray();
			}
		}

		public static byte[] CreateMetadataBytes(Metadata meta)
		{
			var json =
				"{\"name\":\"" + EscapeJson(meta.Name) + "\"," +
				"\"size\":" + meta.Size + "," +
				"\"frameCount\":" + meta.FrameCount + "," +
				"\"compression\":\"" + EscapeJson(meta.Compression) + "\"," +
				"\"sha256\":\"" + EscapeJson(meta.Sha256) + "\"}";

			return Encoding.UTF8.GetBytes(json);
		}

		private static string EscapeJson(string value)
		{
			var result = new StringBuilder();

			foreach (var c in value)
			{
				switch (c)
				{
					case '"':
						result.Append("\\\"");
						break;

					case '\\':
						result.Append("\\\\");
						break;

					case '\b':
						result.Append("\\b");
						break;

					case '\f':
						result.Append("\\f");
						break;

					case '\n':
						result.Append("\\n");
						break;

					case '\r':
						result.Append("\\r");
						break;

					case '\t':
						result.Append("\\t");
						break;

					default:
						if (c < 0x20)
						{
							result.Append("\\u");
							result.Append(((int)c).ToString("X4"));
						}
						else
						{
							result.Append(c);
						}
						break;
				}
			}

			return result.ToString();
		}

		public static List<Frame> SplitFile(
			byte[] data,
			int payloadSize,
			uint transferId)
		{
			if (payloadSize <= 0 || payloadSize > ushort.MaxValue)
			{
				throw new ArgumentOutOfRangeException("payloadSize");
			}

			var frameCountLong = ((long)data.Length + payloadSize - 1) / payloadSize;

			if (frameCountLong > ushort.MaxValue)
			{
				throw new InvalidOperationException("フレーム数が上限の65535を超えています。");
			}

			var frameCount = (ushort)frameCountLong;
			var frames = new List<Frame>();

			for (var i = 0; i < frameCount; i ++)
			{
				var offset = i * payloadSize;
				var size = Math.Min(payloadSize, data.Length - offset);
				var payload = new byte[size];

				Array.Copy(data, offset, payload, 0, size);

				frames.Add
				(
					new Frame
					{
						Type = 1,
						TransferId = transferId,
						Index = (ushort)i,
						Count = frameCount,
						Payload = payload,
					}
				);
			}

			return frames;
		}

		public static List<Frame> CreateAllFrames(string path, int payloadSize)
		{
			var data = File.ReadAllBytes(path);

			var transferId = CreateTransferId();
			var dataFrames = SplitFile(data, payloadSize, transferId);

			string sha256;

			using (var algorithm = SHA256.Create())
			{
				var hash = algorithm.ComputeHash(data);
				var builder = new StringBuilder(hash.Length * 2);

				for (var i = 0; i < hash.Length; i ++)
				{
					builder.Append(hash[i].ToString("x2"));
				}

				sha256 = builder.ToString();
			}

			var meta = new Metadata
			{
				Name = Path.GetFileName(path),
				Size = data.LongLength,
				FrameCount = dataFrames.Count,
				Compression = "none",
				Sha256 = sha256,
			};

			var metaFrame = new Frame
			{
				Type = 0,
				TransferId = transferId,
				Index = 0,
				Count = 1,
				Payload = CreateMetadataBytes(meta),
			};

			var allFrames = new List<Frame>();

			allFrames.Add(metaFrame);
			allFrames.AddRange(dataFrames);

			return allFrames;
		}

		private static uint CreateTransferId()
		{
			var buffer = new byte[4];

			using (var rng = RandomNumberGenerator.Create())
			{
				rng.GetBytes(buffer);
			}

			return BitConverter.ToUInt32(buffer, 0);
		}
	}

	public static class QrEncoder
	{
		public static Bitmap CreateQr(byte[] data)
		{
			var text = Convert.ToBase64String(data);

			using (var generator = new QRCodeGenerator())
			using (var qrData = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.L))
			{
				var qr = new QRCode(qrData);

				return qr.GetGraphic(8);
			}
		}
	}

	public class MainForm : Form
	{
		private Button openButton;
		private Button playPauseButton;
		private Button stopButton;
		private ComboBox speedComboBox;
		private PictureBox pictureBox;
		private Label statusLabel;
		private Timer timer;

		private List<Frame> frames;
		private int frameIndex;
		private bool isPaused;
		private bool isSending;

		private const int PayloadSize = 300;

		private readonly int[] frameRates = new int[]
		{
			1,
			2,
			4,
			8,
			12,
			16,
		};

		public MainForm()
		{
			this.Text = "QRTransfer";
			this.ClientSize = new Size(700, 600);
			this.MinimumSize = new Size(500, 400);
			this.StartPosition = FormStartPosition.CenterScreen;

			var layout = new TableLayoutPanel();
			layout.Dock = DockStyle.Fill;
			layout.ColumnCount = 1;
			layout.RowCount = 3;
			layout.Margin = new Padding(0);
			layout.Padding = new Padding(0);

			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 85F));

			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55F));

			var buttonPanel = new FlowLayoutPanel();
			buttonPanel.Dock = DockStyle.Fill;
			buttonPanel.FlowDirection = FlowDirection.LeftToRight;
			buttonPanel.WrapContents = true;
			buttonPanel.Padding = new Padding(5);
			buttonPanel.Margin = new Padding(0);

			openButton = new Button();
			openButton.Text = "ファイルを開く";
			openButton.AutoSize = true;
			openButton.Click += OpenButton_Click;

			playPauseButton = new Button();
			playPauseButton.Text = "送信開始";
			playPauseButton.AutoSize = true;
			playPauseButton.Enabled = false;
			playPauseButton.Click += PlayPauseButton_Click;

			stopButton = new Button();
			stopButton.Text = "停止";
			stopButton.AutoSize = true;
			stopButton.Enabled = false;
			stopButton.Click += StopButton_Click;

			speedComboBox = new ComboBox();
			speedComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
			speedComboBox.Width = 90;
			speedComboBox.Margin = new Padding(5, 7, 3, 3);

			for (var i = 0; i < frameRates.Length; i ++)
			{
				speedComboBox.Items.Add(frameRates[i].ToString() + " fps");
			}

			speedComboBox.SelectedIndex = 1;
			speedComboBox.SelectedIndexChanged += SpeedComboBox_SelectedIndexChanged;

			buttonPanel.Controls.Add(openButton);
			buttonPanel.Controls.Add(playPauseButton);
			buttonPanel.Controls.Add(stopButton);
			buttonPanel.Controls.Add(speedComboBox);

			var picturePanel = new Panel();
			picturePanel.Dock = DockStyle.Fill;
			picturePanel.Padding = new Padding(5);

			pictureBox = new PictureBox();
			pictureBox.Dock = DockStyle.Fill;
			pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
			pictureBox.Margin = new Padding(0);

			picturePanel.Controls.Add(pictureBox);

			statusLabel = new Label();
			statusLabel.Dock = DockStyle.Fill;
			statusLabel.TextAlign = ContentAlignment.MiddleLeft;
			statusLabel.AutoEllipsis = true;
			statusLabel.Padding = new Padding(5, 0, 5, 0);
			statusLabel.Text = "ファイルを選択してください。";

			layout.Controls.Add(buttonPanel, 0, 0);
			layout.Controls.Add(picturePanel, 0, 1);
			layout.Controls.Add(statusLabel, 0, 2);

			this.Controls.Add(layout);

			timer = new Timer();
			timer.Interval = 500;
			timer.Tick += Timer_Tick;
		}

		private void OpenButton_Click(object sender, EventArgs e)
		{
			using (var dialog = new OpenFileDialog())
			{
				dialog.Title = "送信するファイルを選択";
				dialog.CheckFileExists = true;
				dialog.Multiselect = false;

				if (dialog.ShowDialog() != DialogResult.OK)
				{
					return;
				}

				try
				{
					timer.Stop();

					isPaused = false;
					isSending = false;

					frames = FrameEncoder.CreateAllFrames(dialog.FileName, PayloadSize);

					frameIndex = 0;

					ShowFrame();
					UpdateControls();

					statusLabel.Text = string.Format("ファイル: {0} / データフレーム数: {1} / 開始待ち", Path.GetFileName(dialog.FileName), frames.Count - 1);
				}
				catch (Exception ex)
				{
					UpdateControls();

					MessageBox.Show(this, ex.Message, "ファイル読み込みエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		private void PlayPauseButton_Click(object sender, EventArgs e)
		{
			if (frames == null || frames.Count == 0)
			{
				return;
			}

			if (!isSending)
			{
				timer.Stop();

				frameIndex = 0;
				isPaused = false;
				isSending = true;

				ShowFrame();
				UpdateTimerInterval();
				timer.Start();

				UpdateControls();
				UpdateStatus("送信中");

				return;
			}

			if (isPaused)
			{
				isPaused = false;

				UpdateTimerInterval();
				timer.Start();

				UpdateControls();
				UpdateStatus("送信中");

				return;
			}

			timer.Stop();
			isPaused = true;

			UpdateControls();
			UpdateStatus("一時停止中");
		}

		private void StopButton_Click(object sender, EventArgs e)
		{
			timer.Stop();

			isPaused = false;
			isSending = false;
			frameIndex = 0;

			if (frames != null && frames.Count > 0)
			{
				ShowFrame();
			}

			UpdateControls();
			UpdateStatus("停止");
		}

		private void SpeedComboBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			UpdateTimerInterval();

			if (isPaused)
			{
				UpdateStatus("一時停止中");
			}
			else if (isSending)
			{
				UpdateStatus("送信中");
			}
		}

		private void UpdateTimerInterval()
		{
			if (speedComboBox.SelectedIndex < 0)
			{
				return;
			}

			var fps = frameRates[speedComboBox.SelectedIndex];

			timer.Interval = 1000 / fps;
		}

		private void Timer_Tick(object sender, EventArgs e)
		{
			if (!isSending || isPaused || frames == null || frames.Count == 0)
			{
				timer.Stop();
				return;
			}

			if (frameIndex + 1 >= frames.Count)
			{
				timer.Stop();

				isSending = false;
				isPaused = false;

				var oldImage = pictureBox.Image;
				pictureBox.Image = null;

				if (oldImage != null)
				{
					oldImage.Dispose();
				}

				UpdateControls();
				UpdateStatus("送信終了");

				return;
			}

			frameIndex ++;

			ShowFrame();
			UpdateStatus("送信中");
		}

		private void ShowFrame()
		{
			if (frames == null || frames.Count == 0)
			{
				return;
			}

			var data = FrameEncoder.SerializeFrame(frames[frameIndex]);

			var bitmap = QrEncoder.CreateQr(data);

			var oldImage = pictureBox.Image;
			pictureBox.Image = bitmap;

			if (oldImage != null)
			{
				oldImage.Dispose();
			}
		}

		private void UpdateControls()
		{
			var hasFrames = frames != null && frames.Count > 0;

			openButton.Enabled = !isSending;
			playPauseButton.Enabled = hasFrames;
			stopButton.Enabled = isSending;
			speedComboBox.Enabled = true;

			if (!hasFrames || !isSending)
			{
				playPauseButton.Text = "送信開始";
			}
			else if (isPaused)
			{
				playPauseButton.Text = "再開";
			}
			else
			{
				playPauseButton.Text = "一時停止";
			}
		}

		private void UpdateStatus(string state)
		{
			if (frames == null || frames.Count == 0)
			{
				statusLabel.Text = state;
				return;
			}

			var fps = frameRates[speedComboBox.SelectedIndex];

			statusLabel.Text = string.Format("{0} / フレーム: {1} / {2} / 速度: {3} fps", state, frameIndex + 1, frames.Count, fps);
		}

		protected override void OnFormClosed(FormClosedEventArgs e)
		{
			timer.Stop();
			timer.Dispose();

			if (pictureBox.Image != null)
			{
				pictureBox.Image.Dispose();
				pictureBox.Image = null;
			}

			base.OnFormClosed(e);
		}
	}

	internal static class Program
	{
		[STAThread]
		private static void Main()
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new MainForm());
		}
	}
}
