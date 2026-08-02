using System;
using System.Drawing;
using System.Windows.Forms;
using CloudflareR2Uploader.Controls;

namespace CloudflareR2Uploader.Forms
{
    public partial class MainForm
    {
        private PageTransitionOverlay _pageTransitionOverlay;

        private void InitializePageTransition()
        {
            _pageTransitionOverlay = new PageTransitionOverlay
            {
                Name = "pageTransitionOverlay",
                AccessibleName = "Page transition",
                Visible = false
            };
            Controls.Add(_pageTransitionOverlay);
            Resize += OnMainFormTransitionResize;
        }

        private bool IsPageTransitionRunning
        {
            get { return _pageTransitionOverlay != null && _pageTransitionOverlay.IsAnimating; }
        }

        private Bitmap CaptureCurrentPageSnapshot(bool browserPage)
        {
            if (!IsHandleCreated || bucketBrowserPagePanel.Width <= 0 || bucketBrowserPagePanel.Height <= 0)
                return null;

            Bitmap snapshot = new Bitmap(
                bucketBrowserPagePanel.Width,
                bucketBrowserPagePanel.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

            try
            {
                if (browserPage)
                {
                    bucketBrowserPagePanel.DrawToBitmap(
                        snapshot,
                        new Rectangle(Point.Empty, bucketBrowserPagePanel.Size));
                }
                else
                {
                    DrawPageControl(snapshot, dropZone);
                    DrawPageControl(snapshot, destinationCard);
                    DrawPageControl(snapshot, queueCard);
                    DrawPageControl(snapshot, statusCard);
                }
                return snapshot;
            }
            catch (ArgumentException)
            {
                snapshot.Dispose();
                return null;
            }
        }

        private void DrawPageControl(Bitmap target, Control control)
        {
            Rectangle destination = new Rectangle(
                control.Left - bucketBrowserPagePanel.Left,
                control.Top - bucketBrowserPagePanel.Top,
                control.Width,
                control.Height);
            if (destination.Width > 0 && destination.Height > 0)
                control.DrawToBitmap(target, destination);
        }

        private void StartPageTransition(Bitmap outgoing, bool towardsFiles)
        {
            if (_pageTransitionOverlay == null ||
                !SystemInformation.IsMenuAnimationEnabled ||
                WindowState == FormWindowState.Minimized)
            {
                if (outgoing != null) outgoing.Dispose();
                return;
            }

            PerformLayout();
            Bitmap incoming = CaptureCurrentPageSnapshot(towardsFiles);
            if (outgoing == null || incoming == null)
            {
                if (outgoing != null) outgoing.Dispose();
                if (incoming != null) incoming.Dispose();
                return;
            }

            _pageTransitionOverlay.Bounds = new Rectangle(
                rootLayout.Left + bucketBrowserPagePanel.Left,
                rootLayout.Top + bucketBrowserPagePanel.Top,
                bucketBrowserPagePanel.Width,
                bucketBrowserPagePanel.Height);
            _pageTransitionOverlay.Start(outgoing, incoming, towardsFiles);
        }

        private void OnMainFormTransitionResize(object sender, EventArgs e)
        {
            if (_pageTransitionOverlay != null) _pageTransitionOverlay.Stop();
        }

        private void DisposePageTransition()
        {
            Resize -= OnMainFormTransitionResize;
            if (_pageTransitionOverlay != null)
            {
                _pageTransitionOverlay.Dispose();
                _pageTransitionOverlay = null;
            }
        }
    }
}
