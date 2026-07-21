using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ScreenDraw
{
	internal enum AccentState
	{
		ACCENT_DISABLED = 0,
		ACCENT_ENABLE_GRADIENT = 1,
		ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
		ACCENT_ENABLE_BLURBEHIND = 3,
		ACCENT_INVALID_STATE = 4
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct AccentPolicy
	{
		public AccentState AccentState;
		public int AccentFlags;
		public int GradientColor;
		public int AnimationId;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct WindowCompositionAttributeData
	{
		public WindowCompositionAttribute Attribute;
		public IntPtr Data;
		public int SizeOfData;
	}

	internal enum WindowCompositionAttribute
    { 
		WCA_ACCENT_POLICY = 3
	}

	internal enum DrawMode
	{
		Desktop,
		Pencil,
		Rectangle
	}

	public partial class MainWindow : Window
	{
        private const int HotkeyDesktop = 1;
        private const int HotkeyPencil = 2;
        private const int HotkeyRectangle = 3;
        private const int HotkeyUndo = 4;
        private const int WmHotkey = 0x0312;
        private const int GwlExstyle = -20;
        private const int WsExTransparent = 0x00000020;

        private readonly Canvas drawCanvas;
        private readonly StackPanel controlsPanel;
        private readonly System.Windows.Controls.Button closeButton;
        private readonly System.Windows.Controls.Button saveButton;
        private readonly TextBlock messagesText;
        private readonly WrapPanel shootPanel;

        private System.Windows.Point currentPoint = new System.Windows.Point();
        private System.Windows.Media.Color colorLine = Colors.Black;
        private DrawMode drawMode = DrawMode.Pencil;
        private bool isDrawingRectangle;
        private bool isDesktopMode;
        private System.Windows.Shapes.Rectangle previewRectangle;
        private List<UIElement> currentStroke;
        private readonly Stack<List<UIElement>> undoStack = new Stack<List<UIElement>>();
        private HotkeySettings hotkeySettings;
        private System.Windows.Controls.TextBox recordingHotkeyBox;
        private DispatcherTimer messageTimer;
        private DispatcherTimer desktopMouseTimer;
        private bool hwndHookRegistered;
        private bool clickThroughActive;

        [DllImport("user32.dll")]
		internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

		public MainWindow()
		{
            InitializeComponent();

            drawCanvas = canv;
            controlsPanel = stakPControls;
            closeButton = btnClose;
            saveButton = btnSave;
            messagesText = txtMessages;
            shootPanel = wpShoot;

            hotkeySettings = HotkeySettings.Load();
        }
		
		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			EnableBlur();
            LoadSettingsToUi();
            RegisterGlobalHotkeys();
            SetDrawMode(DrawMode.Pencil);
		}

        private void Window_Closed(object sender, EventArgs e)
        {
            StopDesktopMouseTracking();
            UnregisterAllHotkeys();
        }

        private void LoadSettingsToUi()
        {
            txtHotkeyDesktop.Text = hotkeySettings.DesktopHotkey;
            txtHotkeyPencil.Text = hotkeySettings.PencilHotkey;
            txtHotkeyRectangle.Text = hotkeySettings.RectangleHotkey;
            txtHotkeyUndo.Text = hotkeySettings.UndoHotkey;
        }

        private void UnregisterAllHotkeys()
        {
            var windowHelper = new WindowInteropHelper(this);
            UnregisterHotKey(windowHelper.Handle, HotkeyDesktop);
            UnregisterHotKey(windowHelper.Handle, HotkeyPencil);
            UnregisterHotKey(windowHelper.Handle, HotkeyRectangle);
            UnregisterHotKey(windowHelper.Handle, HotkeyUndo);
        }

        private void RegisterGlobalHotkeys()
        {
            var windowHelper = new WindowInteropHelper(this);
            var source = HwndSource.FromHwnd(windowHelper.Handle);
            if (source != null && !hwndHookRegistered)
            {
                source.AddHook(HwndHook);
                hwndHookRegistered = true;
            }

            UnregisterAllHotkeys();
            RegisterConfiguredHotkey(windowHelper.Handle, HotkeyDesktop, hotkeySettings.DesktopHotkey);
            RegisterConfiguredHotkey(windowHelper.Handle, HotkeyPencil, hotkeySettings.PencilHotkey);
            RegisterConfiguredHotkey(windowHelper.Handle, HotkeyRectangle, hotkeySettings.RectangleHotkey);
            RegisterConfiguredHotkey(windowHelper.Handle, HotkeyUndo, hotkeySettings.UndoHotkey);
        }

        private static void RegisterConfiguredHotkey(IntPtr handle, int id, string hotkeyText)
        {
            if (HotkeySettings.TryParse(hotkeyText, out uint modifiers, out uint virtualKey))
                RegisterHotKey(handle, id, modifiers, virtualKey);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmHotkey)
            {
                switch (wParam.ToInt32())
                {
                    case HotkeyDesktop:
                        SetDrawMode(DrawMode.Desktop);
                        handled = true;
                        break;
                    case HotkeyPencil:
                        SetDrawMode(DrawMode.Pencil);
                        handled = true;
                        break;
                    case HotkeyRectangle:
                        SetDrawMode(DrawMode.Rectangle);
                        handled = true;
                        break;
                    case HotkeyUndo:
                        UndoLastAction();
                        handled = true;
                        break;
                }
            }

            return IntPtr.Zero;
        }

        private void SetDrawMode(DrawMode mode)
        {
            CancelCurrentDrawing();
            drawMode = mode;

            if (mode == DrawMode.Desktop)
            {
                isDesktopMode = true;
                drawCanvas.IsHitTestVisible = false;
                StartDesktopMouseTracking();
                ShowModeMessage("Desktop mode - chuột dùng bình thường (" + hotkeySettings.DesktopHotkey + ")");
                return;
            }

            isDesktopMode = false;
            StopDesktopMouseTracking();
            drawCanvas.IsHitTestVisible = true;

            if (mode == DrawMode.Pencil)
                ShowModeMessage("Pencil mode (" + hotkeySettings.PencilHotkey + ")");
            else
                ShowModeMessage("Rectangle mode (" + hotkeySettings.RectangleHotkey + ")");
        }

        private void StartDesktopMouseTracking()
        {
            if (desktopMouseTimer == null)
            {
                desktopMouseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                desktopMouseTimer.Tick += DesktopMouseTimer_Tick;
            }

            desktopMouseTimer.Start();
            UpdateDesktopClickThrough();
        }

        private void StopDesktopMouseTracking()
        {
            desktopMouseTimer?.Stop();
            SetClickThrough(false);
        }

        private void DesktopMouseTimer_Tick(object sender, EventArgs e)
        {
            UpdateDesktopClickThrough();
        }

        private void UpdateDesktopClickThrough()
        {
            if (!isDesktopMode)
            {
                SetClickThrough(false);
                return;
            }

            var mousePos = System.Windows.Forms.Control.MousePosition;
            var screenPoint = new System.Windows.Point(mousePos.X, mousePos.Y);
            SetClickThrough(!IsPointOverInteractiveControls(screenPoint));
        }

        private void SetClickThrough(bool enabled)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || clickThroughActive == enabled)
                return;

            IntPtr style = GetWindowLongPtr(hwnd, GwlExstyle);
            long styleValue = style.ToInt64();

            if (enabled)
                styleValue |= WsExTransparent;
            else
                styleValue &= ~WsExTransparent;

            SetWindowLongPtr(hwnd, GwlExstyle, new IntPtr(styleValue));
            clickThroughActive = enabled;
        }

        private bool IsPointOverInteractiveControls(System.Windows.Point screenPoint)
        {
            if (IsPointInsideElement(controlsPanel, screenPoint))
                return true;

            if (messagesText.Visibility == Visibility.Visible && IsPointInsideElement(messagesText, screenPoint))
                return true;

            if (settingsPanel.Visibility == Visibility.Visible && IsPointInsideElement(settingsPanel, screenPoint))
                return true;

            return false;
        }

        private static bool IsPointInsideElement(FrameworkElement element, System.Windows.Point screenPoint)
        {
            if (element == null || element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
                return false;

            var topLeft = element.PointToScreen(new System.Windows.Point(0, 0));
            var bottomRight = element.PointToScreen(new System.Windows.Point(element.ActualWidth, element.ActualHeight));
            return screenPoint.X >= topLeft.X && screenPoint.X <= bottomRight.X &&
                   screenPoint.Y >= topLeft.Y && screenPoint.Y <= bottomRight.Y;
        }

        private void CancelCurrentDrawing()
        {
            isDrawingRectangle = false;
            previewRectangle = null;
            currentStroke = null;
        }

        private void UndoLastAction()
        {
            if (isDesktopMode)
            {
                ShowModeMessage("Không thể hoàn tác khi đang ở chế độ Desktop");
                return;
            }

            if (undoStack.Count == 0)
            {
                ShowModeMessage("Không có thao tác để hoàn tác");
                return;
            }

            var action = undoStack.Pop();
            foreach (var element in action)
                drawCanvas.Children.Remove(element);

            ShowModeMessage("Đã hoàn tác (" + hotkeySettings.UndoHotkey + ")");
        }

        private void CommitStroke()
        {
            if (currentStroke != null && currentStroke.Count > 0)
                undoStack.Push(currentStroke);
            currentStroke = null;
        }

        private void CommitShape(UIElement element)
        {
            undoStack.Push(new List<UIElement> { element });
        }

        private void ShowModeMessage(string text)
        {
            messagesText.Text = text;
            messagesText.Visibility = Visibility.Visible;

            if (messageTimer == null)
            {
                messageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                messageTimer.Tick += (s, e) =>
                {
                    messagesText.Visibility = Visibility.Collapsed;
                    messageTimer.Stop();
                };
            }

            messageTimer.Stop();
            messageTimer.Start();
        }

        private SolidColorBrush CreateStrokeBrush()
        {
            return new SolidColorBrush(colorLine);
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            settingsPanel.Visibility = settingsPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void btnCloseSettings_Click(object sender, RoutedEventArgs e)
        {
            recordingHotkeyBox = null;
            settingsPanel.Visibility = Visibility.Collapsed;
            LoadSettingsToUi();
        }

        private void btnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            hotkeySettings.DesktopHotkey = txtHotkeyDesktop.Text.Trim();
            hotkeySettings.PencilHotkey = txtHotkeyPencil.Text.Trim();
            hotkeySettings.RectangleHotkey = txtHotkeyRectangle.Text.Trim();
            hotkeySettings.UndoHotkey = txtHotkeyUndo.Text.Trim();
            hotkeySettings.Save();
            RegisterGlobalHotkeys();
            settingsPanel.Visibility = Visibility.Collapsed;
            recordingHotkeyBox = null;
            ShowModeMessage("Đã lưu phím tắt");
        }

        private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
        {
            recordingHotkeyBox = (System.Windows.Controls.TextBox)sender;
            recordingHotkeyBox.Text = "Nhấn phím tắt mới...";
        }

        private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!ReferenceEquals(sender, recordingHotkeyBox))
                return;

            e.Handled = true;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            var textBox = (System.Windows.Controls.TextBox)sender;
            textBox.Text = HotkeySettings.Format(Keyboard.Modifiers, key);
            recordingHotkeyBox = null;
        }
		
		internal void EnableBlur()
		{
			var windowHelper = new WindowInteropHelper(this);
			
			var accent = new AccentPolicy();
			accent.AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND;

			var accentStructSize = Marshal.SizeOf(accent);

			var accentPtr = Marshal.AllocHGlobal(accentStructSize);
			Marshal.StructureToPtr(accent, accentPtr, false);

			var data = new WindowCompositionAttributeData();
			data.Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY;
			data.SizeOfData = accentStructSize;
			data.Data = accentPtr;
			
			SetWindowCompositionAttribute(windowHelper.Handle, ref data);

			Marshal.FreeHGlobal(accentPtr);
		}

        private void canv_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (isDesktopMode || e.ButtonState != MouseButtonState.Pressed)
                return;

            currentPoint = e.GetPosition(this);

            if (drawMode == DrawMode.Pencil)
            {
                currentStroke = new List<UIElement>();
                return;
            }

            if (drawMode == DrawMode.Rectangle)
            {
                previewRectangle = new System.Windows.Shapes.Rectangle
                {
                    Stroke = CreateStrokeBrush(),
                    StrokeThickness = 2,
                    Fill = System.Windows.Media.Brushes.Transparent
                };
                Canvas.SetLeft(previewRectangle, currentPoint.X);
                Canvas.SetTop(previewRectangle, currentPoint.Y);
                drawCanvas.Children.Add(previewRectangle);
                isDrawingRectangle = true;
            }
        }

        private void canv_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (isDesktopMode)
                return;

            if (drawMode == DrawMode.Pencil)
            {
                if (e.LeftButton != MouseButtonState.Pressed || currentStroke == null)
                    return;

                var point = e.GetPosition(this);
                var line = new Line
                {
                    Stroke = CreateStrokeBrush(),
                    StrokeThickness = 2,
                    X1 = currentPoint.X,
                    Y1 = currentPoint.Y,
                    X2 = point.X,
                    Y2 = point.Y
                };
                currentPoint = point;
                drawCanvas.Children.Add(line);
                currentStroke.Add(line);
                return;
            }

            if (drawMode == DrawMode.Rectangle && isDrawingRectangle && previewRectangle != null)
                UpdatePreviewRectangle(e.GetPosition(this));
        }

        private void canv_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (isDesktopMode)
                return;

            if (drawMode == DrawMode.Pencil)
            {
                CommitStroke();
                return;
            }

            if (drawMode != DrawMode.Rectangle || !isDrawingRectangle || previewRectangle == null)
                return;

            UpdatePreviewRectangle(e.GetPosition(this));
            CommitShape(previewRectangle);
            isDrawingRectangle = false;
            previewRectangle = null;
        }

        private void UpdatePreviewRectangle(System.Windows.Point endPoint)
        {
            double x = Math.Min(currentPoint.X, endPoint.X);
            double y = Math.Min(currentPoint.Y, endPoint.Y);
            double width = Math.Abs(endPoint.X - currentPoint.X);
            double height = Math.Abs(endPoint.Y - currentPoint.Y);

            Canvas.SetLeft(previewRectangle, x);
            Canvas.SetTop(previewRectangle, y);
            previewRectangle.Width = width;
            previewRectangle.Height = height;
        }

        private Bitmap CaptureScreen()
        {
            var bmpScreenshot = new Bitmap(Screen.PrimaryScreen.Bounds.Width,
                   Screen.PrimaryScreen.Bounds.Height,
                   System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var gfxScreenshot = Graphics.FromImage(bmpScreenshot);
            gfxScreenshot.CopyFromScreen(Screen.PrimaryScreen.Bounds.X,
                    Screen.PrimaryScreen.Bounds.Y,
                    0,
                    0,
                    Screen.PrimaryScreen.Bounds.Size,
                    CopyPixelOperation.SourceCopy);
            return bmpScreenshot;
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }

        private void cp_ChangeColor(object sender, RoutedEventArgs e)
        {
            System.Windows.Media.Brush brush = ((System.Windows.Controls.Button)e.OriginalSource).Background;
            colorLine = ((SolidColorBrush)brush).Color;
        }


        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            controlsPanel.Visibility = Visibility.Hidden;
            closeButton.Visibility = Visibility.Hidden;
            saveButton.Visibility = Visibility.Hidden;
            settingsPanel.Visibility = Visibility.Collapsed;

            string path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            path += @"\ScreenDraw_" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + ".bmp";
            DispatcherTimer mdtTakeSS = new DispatcherTimer();
            mdtTakeSS.Interval = new TimeSpan(0, 0, 0, 0, 50);
            mdtTakeSS.Tick += delegate (object s, EventArgs args) {
                CaptureScreen().Save(path);
                messagesText.Text = string.Format("Screenshot saved on {0}", path);
                messagesText.Visibility = Visibility.Visible;
                shootPanel.Visibility = Visibility.Visible;
                mdtTakeSS.Stop();
            };

            DispatcherTimer mdtShowControls = new DispatcherTimer();
            mdtShowControls.Interval = new TimeSpan(0, 0, 0, 0, 100);
            mdtShowControls.Tick += delegate (object s, EventArgs args) {
                shootPanel.Visibility = Visibility.Hidden;
                controlsPanel.Visibility = Visibility.Visible;
                closeButton.Visibility = Visibility.Visible;
                saveButton.Visibility = Visibility.Visible;
                mdtShowControls.Stop();
            };

            DispatcherTimer mdtHideMessge = new DispatcherTimer();
            mdtHideMessge.Interval = new TimeSpan(0, 0, 0, 5, 0);
            mdtHideMessge.Tick += delegate (object s, EventArgs args) {
                messagesText.Text = string.Format("Screenshot saved on {0}", path);
                messagesText.Visibility = Visibility.Hidden;
                mdtHideMessge.Stop();
            };

            mdtTakeSS.Start();
            mdtShowControls.Start();
            mdtHideMessge.Start();
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CancelCurrentDrawing();
                undoStack.Clear();
                drawCanvas.Children.Clear();
            }
        }
    }
}
