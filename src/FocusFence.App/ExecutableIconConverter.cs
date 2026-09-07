using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace FocusFence.App;

public sealed class ExecutableIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            if (value is not string path || !System.IO.File.Exists(path)) return null;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(24, 24));
            image.Freeze(); return image;
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { return null; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
