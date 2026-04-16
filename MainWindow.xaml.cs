using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using System.Drawing;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using AI_COMPNENT_SAFTY.Services;

using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

using System.Management;

namespace AI_COMPNENT_SAFTY
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        private CameraService cameraService;
        private DispatcherTimer timer;
        private ESDDetectionService service = new ESDDetectionService();
        private static readonly HttpClient client = new HttpClient();
        private List<int> availableCameras = new List<int>();
        private bool isProcessing = false;

        private ObservableCollection<MailLog> mailLogs = new ObservableCollection<MailLog>();
        private int lastMailCount = 0; // ADD THIS ALSO

        private int warningCount = 0;
        private string lastViolationType = "";
        private int lastWarningSecond = -1;
        public class MailLog
        {
            public string Time { get; set; }
            public string Status { get; set; }
            public string Message { get; set; }
        }

        public MainWindow()
        {
           
            InitializeComponent(); // MUST BE FIRST
            LoadCameras();
            CameraNameText.Text = GetCameraName(); // ✔ ADD HERE

            cameraService = new CameraService();

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(150);
            timer.Tick += UpdateFrame;
            

            MailHistoryGrid.ItemsSource = mailLogs;
        }

        private void TestLogic()
        {
            // Simulate detection (replace later with camera)
            service.Process(0, 2);

            MessageBox.Show(service.Status);
        }

        

        private async void UpdateFrame(object sender, EventArgs e)
        {
            if (cameraService == null)
                return;

            var frame = cameraService.GetFrame();

            if (frame == null || frame.Empty())
                return;

            // Show camera frame
            CameraImage.Source = ConvertMatToBitmapSource(frame);

            if (isProcessing) return;
            isProcessing = true;

            try
            {
                var result = await SendToPython(frame);

                if (result == null)
                {
                    isProcessing = false;
                    return;
                }

                // =========================
                // CONFIG (SAFE PARSING)
                // =========================
                string model = result.config?.model ?? "N/A";
                double conf = result.config?.confidence ?? 0;
                bool roi = result.config?.roi ?? false;

                ConfigText.Text =
                    $"Model: {model} | Conf: {conf:0.00} | ROI: {(roi ? "ON" : "OFF")}";

                // =========================
                // PROCESS RESULT
                // =========================
                int wearing = result.wearing;
                int notWearing = result.not_wearing;

                service.Process(wearing, notWearing);

                // =========================
                // STATUS UI
                // =========================
                if (service.Status == "ESD_WEARING")
                {
                    StatusText.Text = "SAFE";
                    StatusText.Foreground = System.Windows.Media.Brushes.LimeGreen;
                }
                else if (service.Status == "VIOLATION")
                {
                    StatusText.Text = "VIOLATION";
                    StatusText.Foreground = System.Windows.Media.Brushes.Red;
                }
                else
                {
                    StatusText.Text = "NO PERSON";
                    StatusText.Foreground = System.Windows.Media.Brushes.Gray;
                }

                // =========================
                // COUNTERS
                // =========================
                MailText.Text = service.MailCount.ToString();
                TimeText.Text = service.ViolationTime.ToString("0.0");

                // =========================
                // 🔥 PYTHON EXACT LOGIC (MATCH)
                // =========================
                if (service.Status == "VIOLATION")
                {
                    string violationType = "";

                    if (notWearing > 1 && wearing >= 1)
                        violationType = "Multiple unsafe hands detected";
                    else
                        violationType = "No ESD band detected";

                    // Reset if type changed
                    if (lastViolationType != violationType)
                    {
                        warningCount = 0;
                    }

                    lastViolationType = violationType;

                    double elapsed = service.ViolationTime;

              
                    if (elapsed >= 10)   // VIOLATION_THRESHOLD
                    {
                        warningCount++;

                        mailLogs.Add(new MailLog
                        {
                            Time = DateTime.Now.ToString("HH:mm:ss"),
                            Status = "WARNING",
                            Message = $"Warning {warningCount}: {violationType}"
                        });

                        // 🔥 RESET TIMER (IMPORTANT — same as Python)
                        service.ResetViolationTimer();

                        // 🔥 SEND MAIL AFTER 3 WARNINGS
                        if (warningCount >= 3)
                        {
                            await SendMail(
                                "ESD VIOLATION ALERT",
                                violationType
                            );

                            mailLogs.Add(new MailLog
                            {
                                Time = DateTime.Now.ToString("HH:mm:ss"),
                                Status = "MAIL",
                                Message = $"Complaint mail sent: {violationType}"
                            });

                            warningCount = 0;
                        }
                    }
                }
                else if (service.Status == "ESD_WEARING")
                {
                    // FULL RESET (same as Python)
                    warningCount = 0;
                    lastViolationType = "";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally
            {
                isProcessing = false;
            }
        }
        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        private BitmapSource ConvertMatToBitmapSource(Mat mat)
        {
            using (Bitmap bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(mat))
            {
                IntPtr hBitmap = bitmap.GetHbitmap();

                try
                {
                    BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    return bitmapSource;
                }
                finally
                {
                    DeleteObject(hBitmap); // prevent memory leak
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            cameraService.Stop();
            base.OnClosed(e);
        }

        private async Task<ApiResponse> SendToPython(Mat frame)
        {
            try
            {
                var content = new MultipartFormDataContent();

                byte[] imageBytes = frame.ToBytes(".jpg");
                var byteContent = new ByteArrayContent(imageBytes);
                byteContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");

                content.Add(byteContent, "image", "frame.jpg");


                var response = await client.PostAsync("http://127.0.0.1:5000/detect", content);

                if (!response.IsSuccessStatusCode)
                {
                    return new ApiResponse
                    {
                        wearing = 0,
                        not_wearing = 0,
                        config = new Config()
                    };
                }

                var json = await response.Content.ReadAsStringAsync();

                return JsonConvert.DeserializeObject<ApiResponse>(json);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);

                return new ApiResponse
                {
                    wearing = 0,
                    not_wearing = 0,
                    config = new Config()
                };
            }
        }
        public string GetCameraName()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity"))
                {
                    foreach (var device in searcher.Get())
                    {
                        string name = device["Caption"]?.ToString()?.ToLower();

                        if (name != null &&
                            (name.Contains("camera") || name.Contains("webcam")))
                        {
                            // Skip virtual / unwanted devices
                            if (!name.Contains("virtual") && !name.Contains("ir"))
                                return device["Caption"].ToString();
                        }
                    }
                }
            }
            catch
            {
                return "Unknown Camera";
            }

            return "Camera";
        }

        public async Task SendMail(string subject, string body)
        {
            try
            {
                string fromEmail = "t.haridhesai@nashtechlabs.com";
                string password = "NTL@2500"; 

                var message = new MimeMessage();

                message.From.Add(new MailboxAddress("ESD System", fromEmail));
                message.To.Add(new MailboxAddress("", fromEmail)); // change if needed
                message.Subject = subject;

                message.Body = new TextPart("plain")
                {
                    Text = body
                };

                using (var client = new SmtpClient())
                {
                    // 🔥 Proper secure connection (IMPORTANT)
                    await client.ConnectAsync("smtp.office365.com", 587, SecureSocketOptions.StartTls);

                    // 🔥 Required for some corporate environments
                    client.AuthenticationMechanisms.Remove("XOAUTH2");

                    await client.AuthenticateAsync(fromEmail, password);

                    await client.SendAsync(message);
                    await client.DisconnectAsync(true);
                }
            }
            catch (Exception ex)
            {
                // Don’t hide errors
                Console.WriteLine("MAIL ERROR: " + ex.Message);
            }
        }
        public class ApiResponse
        {
            public int wearing { get; set; }
            public int not_wearing { get; set; }
            public Config config { get; set; }
        }

        public class Config
        {
            public string model { get; set; }
            public double confidence { get; set; }
            public bool roi { get; set; }
        }

        //Camera load
        private void LoadCameras()
        {
            CameraList.Items.Clear();
            availableCameras.Clear();

            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity"))
            {
                foreach (var device in searcher.Get())
                {
                    string name = device["Caption"]?.ToString();

                    if (name != null && (name.ToLower().Contains("camera") || name.ToLower().Contains("webcam")))
                    {
                        CameraList.Items.Add(name);
                    }
                }
            }

            for (int i = 0; i < CameraList.Items.Count; i++)
            {
                availableCameras.Add(i);
            }

            if (CameraList.Items.Count > 0)
                CameraList.SelectedIndex = 0;
        }

        //Start cam
        private void StartCamera_Click(object sender, RoutedEventArgs e)
        {
            if (CameraList.SelectedIndex < 0)
                return;

            int selectedCamera = availableCameras[CameraList.SelectedIndex];

            if (cameraService == null)
                cameraService = new CameraService();

            cameraService.Stop();
            cameraService.Start(selectedCamera);

            timer.Start();
        }

        //Stop cam
        private void StopCamera_Click(object sender, RoutedEventArgs e)
        {
            timer.Stop();

            cameraService.Stop();
            CameraImage.Source = null;
        }

    }
}
