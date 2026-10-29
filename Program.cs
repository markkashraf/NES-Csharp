using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        string path = args.Length > 0 ? args[0] : null;
        if (path == null)
        {
            using var d = new OpenFileDialog { Filter = "NES ROM|*.nes" };
            if (d.ShowDialog() != DialogResult.OK) return;
            path = d.FileName;
        }
        try { Application.Run(new EmuForm(new Nes(new Cartridge(path)), Path.GetFileName(path))); }
        catch (Exception e) { MessageBox.Show(e.Message, "NES"); }
    }
}

class EmuForm : Form
{
    readonly Nes nes;
    readonly Bitmap bmp = new(256, 240, PixelFormat.Format32bppRgb);
    volatile bool run = true;

    public EmuForm(Nes n, string title)
    {
        nes = n; Text = title; ClientSize = new Size(768, 720); DoubleBuffered = true;
        new Thread(Loop) { IsBackground = true }.Start();
    }

    // Z=A  X=B  Backspace/Shift=Select  Enter=Start  Arrows=D-pad
    static byte Map(Keys k) => k switch
    {
        Keys.Z => 1, Keys.X => 2, Keys.ShiftKey or Keys.Back => 4, Keys.Enter => 8,
        Keys.Up => 16, Keys.Down => 32, Keys.Left => 64, Keys.Right => 128, _ => 0
    };
    protected override void OnKeyDown(KeyEventArgs e) { nes.Buttons |= Map(e.KeyCode); }
    protected override void OnKeyUp(KeyEventArgs e) { nes.Buttons &= (byte)~Map(e.KeyCode); }
    protected override void OnFormClosing(FormClosingEventArgs e) { run = false; base.OnFormClosing(e); }

    void Loop()
    {
        var sw = Stopwatch.StartNew();
        const double dt = 1000.0 / 60.0988;
        double next = dt;
        while (run)
        {
            nes.RunFrame();
            lock (bmp)
            {
                var bd = bmp.LockBits(new Rectangle(0, 0, 256, 240), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                Marshal.Copy(nes.Ppu.Frame, 0, bd.Scan0, 256 * 240);
                bmp.UnlockBits(bd);
            }
            if (IsHandleCreated) try { BeginInvoke(new Action(() => Invalidate())); } catch { break; }
            double wait = next - sw.Elapsed.TotalMilliseconds;
            if (wait > 2) Thread.Sleep((int)wait - 1);
            while (sw.Elapsed.TotalMilliseconds < next) { }
            next += dt;
            if (sw.Elapsed.TotalMilliseconds - next > 100) next = sw.Elapsed.TotalMilliseconds; // fell behind
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        lock (bmp) e.Graphics.DrawImage(bmp, ClientRectangle);
    }
}
