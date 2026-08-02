using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CloudflareR2Uploader.Theming
{
    /// <summary>
    /// Asks Windows to render native scrollbars using its dark Explorer palette. WinForms
    /// otherwise paints bright legacy scrollbars even when every surrounding control is dark.
    /// </summary>
    internal static class DarkScrollBars
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        [DllImport("user32.dll")]
        private static extern bool GetComboBoxInfo(IntPtr hwndCombo, ref ComboBoxInfo info);

        [StructLayout(LayoutKind.Sequential)]
        private struct ComboBoxInfo
        {
            public int Size;
            public NativeRect Item;
            public NativeRect Button;
            public int ButtonState;
            public IntPtr Combo;
            public IntPtr Edit;
            public IntPtr List;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public static void Apply(Control control)
        {
            if (control == null || control.IsDisposed) return;

            if (control.IsHandleCreated) ApplyHandle(control.Handle);
            control.HandleCreated += delegate
            {
                if (!control.IsDisposed) ApplyHandle(control.Handle);
            };
        }

        public static void ApplyComboDropDown(ComboBox comboBox)
        {
            if (comboBox == null || !comboBox.IsHandleCreated) return;

            ComboBoxInfo info = new ComboBoxInfo { Size = Marshal.SizeOf(typeof(ComboBoxInfo)) };
            if (GetComboBoxInfo(comboBox.Handle, ref info) && info.List != IntPtr.Zero)
                ApplyHandle(info.List);
        }

        private static void ApplyHandle(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;

            try
            {
                int result = SetWindowTheme(handle, "DarkMode_Explorer", null);
                if (result != 0) SetWindowTheme(handle, "Explorer", null);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }
}
