using System.Drawing;
using System.Drawing.Imaging;

namespace PaddleOnWindowsML
{
    internal static class ScreenHelper
    {
        public static Bitmap CaptureScreen()
        {
            Windows.Win32.Graphics.Gdi.HDC sourceDC = default;
            Windows.Win32.Graphics.Gdi.HBITMAP destBitmap = default;
            Windows.Win32.Graphics.Gdi.HDC destDC = default;

            try
            {
                sourceDC = Windows.Win32.PInvoke.GetWindowDC(Windows.Win32.PInvoke.GetDesktopWindow());

                var width = Windows.Win32.PInvoke.GetDeviceCaps(sourceDC, Windows.Win32.Graphics.Gdi.GET_DEVICE_CAPS_INDEX.HORZRES);
                var height = Windows.Win32.PInvoke.GetDeviceCaps(sourceDC, Windows.Win32.Graphics.Gdi.GET_DEVICE_CAPS_INDEX.VERTRES);

                destBitmap = Windows.Win32.PInvoke.CreateCompatibleBitmap(sourceDC, width, height);
                destDC = Windows.Win32.PInvoke.CreateCompatibleDC(sourceDC);

                Windows.Win32.PInvoke.SelectObject(destDC, destBitmap);
                Windows.Win32.PInvoke.BitBlt(destDC, 0, 0, width, height, sourceDC, 0, 0, Windows.Win32.Graphics.Gdi.ROP_CODE.SRCCOPY);

                using var capture = Image.FromHbitmap(destBitmap);
                var bitmap = new Bitmap(capture.Width, capture.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.DrawImageUnscaled(capture, 0, 0);
                }
                return bitmap;
            }
            finally
            {
                Windows.Win32.PInvoke.DeleteDC(destDC);
                Windows.Win32.PInvoke.DeleteObject(destBitmap);
                Windows.Win32.PInvoke.DeleteDC(sourceDC);
            }
        }
    }
}
