using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace RVpn
{
    internal sealed partial class Desktop
    {
        private BitmapSource lastScreenshot;

        private void MountScreenshotShortcut()
        {
            Get<Button>("ScreenshotButton").Click += (sender, args) => CopyScreenshot();
            Get<Button>("SaveScreenshotButton").Click += (sender, args) => SaveScreenshot();
        }

        private void CopyScreenshot()
        {
            var popup = Get<Popup>("ScreenshotResult");
            popup.IsOpen = false;
            lastScreenshot = Art.WindowScreenshot((Border)Window.Content);
            try
            {
                Clipboard.SetImage(lastScreenshot);
                Text("ScreenshotMessage", "Снимок скопирован. Вставь его через Ctrl+V.");
            }
            catch (ExternalException)
            {
                Text("ScreenshotMessage", "Буфер обмена занят. Снимок можно сохранить в PNG.");
            }
            popup.IsOpen = true;
        }

        private void SaveScreenshot()
        {
            var screenshot = lastScreenshot;
            if (screenshot == null) return;
            Get<Popup>("ScreenshotResult").IsOpen = false;
            var picker = new SaveFileDialog
            {
                Title = "Сохранить снимок R VPN", Filter = "Изображение PNG (*.png)|*.png",
                FileName = "R VPN " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".png"
            };
            if (picker.ShowDialog(Window) != true) return;
            try
            {
                using (var file = picker.OpenFile()) Art.WritePng(screenshot, file);
                Text("ScreenshotMessage", "Снимок сохранён.");
            }
            catch (IOException) { Text("ScreenshotMessage", "Не удалось сохранить PNG. Попробуй другую папку."); }
            catch (UnauthorizedAccessException) { Text("ScreenshotMessage", "Нет доступа к папке. Выбери другую."); }
            Get<Popup>("ScreenshotResult").IsOpen = true;
        }
    }
}
