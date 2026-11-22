// Barra delle schede (una per terminale), quella di CorbolPad in versione scura, disegnata a mano: chiusura con × o tasto centrale, riordino trascinando,
// doppio clic sullo spazio vuoto per una nuova scheda, rotellina per scorrere quando non ci stanno tutte.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CorbolPrompt
{
    class TabStrip : Control
    {
        public List<TermTab> Docs = new List<TermTab>();
        public TermTab Active;

        public event Action<TermTab> TabSelected, TabCloseRequested, TabRenameRequested;
        public event Action<TermTab, Point> TabContextMenu;
        public event Action NewRequested, ListRequested, Reordered;
        public event Action<Point> NewMenuRequested;   // freccia accanto al +: nuova scheda con una shell a scelta

        // scura come il terminale: la scheda attiva ha lo stesso fondo dello schermo
        public static readonly Color TabBar = Color.FromArgb(32, 32, 32), TabActive = Color.FromArgb(12, 12, 12), TabHover = Color.FromArgb(44, 44, 44),
            Border = Color.FromArgb(60, 60, 60), Accent = Color.FromArgb(22, 198, 12), Hover = Color.FromArgb(70, 70, 70),
            Text_ = Color.FromArgb(245, 245, 245), TextDim = Color.FromArgb(200, 200, 200);

        readonly float scale;
        int scroll, scrollMax, hoverIndex = -1, dragIndex = -1;
        bool hoverClose, hoverPlus, hoverList, hoverDrop, overflow;
        Point dragStart;
        bool dragging;
        readonly List<Rectangle> rects = new List<Rectangle>();
        Rectangle plusRect, listRect, dropRect;
        readonly ToolTip tip = new ToolTip();
        int tipIndex = -2;

        public TabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            Font = new Font("Segoe UI", 10f);
            Height = S(36);
        }

        int S(float v) { return (int)Math.Round(v * scale); }

        void Layout_()
        {
            rects.Clear();
            int plusW = S(32), dropW = S(26), listW = S(28), pad = S(4);
            int avail = Width - plusW - dropW - pad * 2;
            var widths = new List<int>();
            int total = 0;
            foreach (var d in Docs)
            {
                int w = TextRenderer.MeasureText(d.Title, Font).Width + S(48);
                w = Math.Max(S(120), Math.Min(S(240), w));
                widths.Add(w); total += w;
            }
            if (total > avail && Docs.Count > 0)
            {
                // Si restringono tutte fino a una larghezza minima, poi si scorre
                int each = Math.Max(S(120), avail / Docs.Count);
                total = 0;
                for (int i = 0; i < widths.Count; i++) { widths[i] = Math.Min(widths[i], each); total += widths[i]; }
            }
            overflow = total > avail;
            if (overflow) avail -= listW;
            int maxScroll = Math.Max(0, total - avail);
            scrollMax = overflow ? maxScroll : 0;
            scroll = Math.Max(0, Math.Min(scroll, maxScroll));
            int x = pad - scroll, top = S(4);
            for (int i = 0; i < widths.Count; i++) { rects.Add(new Rectangle(x, top, widths[i], Height - top)); x += widths[i]; }
            int plusX = overflow ? pad + avail : x + S(2);
            plusRect = new Rectangle(plusX, top + S(3), plusW - S(4), Height - top - S(7));
            dropRect = new Rectangle(plusRect.Right, plusRect.Top, dropW - S(2), plusRect.Height);
            listRect = overflow ? new Rectangle(Width - listW - pad, top + S(3), listW, Height - top - S(7)) : Rectangle.Empty;
        }

        public void EnsureVisible(TermTab d)
        {
            Layout_();
            int i = Docs.IndexOf(d);
            if (i < 0 || !overflow) { Invalidate(); return; }
            int pad = S(4), right = plusRect.Left;
            if (rects[i].Left < pad) scroll -= pad - rects[i].Left;
            else if (rects[i].Right > right) scroll += rects[i].Right - right;
            Invalidate();
        }

        Rectangle CloseRect(Rectangle r)
        {
            int s = S(20);
            return new Rectangle(r.Right - s - S(6), r.Top + (r.Height - s) / 2, s, s);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Layout_();
            var g = e.Graphics;
            g.Clear(TabBar);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int clipRight = overflow ? plusRect.Left : Width;
            for (int i = 0; i < Docs.Count; i++)
            {
                var r = rects[i];
                if (r.Right < 0 || r.Left > clipRight) continue;
                var d = Docs[i];
                bool act = d == Active, hov = i == hoverIndex;
                g.SetClip(new Rectangle(0, 0, clipRight, Height));
                if (act || hov)
                {
                    using (var path = RoundTop(new Rectangle(r.X + 1, r.Y, r.Width - 2, r.Height + 4), S(6)))
                    using (var b = new SolidBrush(act ? TabActive : TabHover)) g.FillPath(b, path);
                }
                else if (i + 1 < Docs.Count && Docs[i + 1] != Active && i + 1 != hoverIndex)
                    using (var p = new Pen(Border)) g.DrawLine(p, r.Right - 1, r.Top + S(8), r.Right - 1, r.Bottom - S(8));

                var cr = CloseRect(r);
                var tr = new Rectangle(r.X + S(12), r.Y, cr.Left - r.X - S(14), r.Height);
                // comando lungo finito mentre la scheda non era in vista: pallino verde (riuscito) o rosso (fallito) prima del nome
                if (d.Notice != 0)
                {
                    int ds = S(8);
                    using (var b = new SolidBrush(d.Notice == 2 ? Color.FromArgb(231, 72, 86) : Accent))
                        g.FillEllipse(b, tr.X, r.Y + (r.Height - ds) / 2, ds, ds);
                    tr = new Rectangle(tr.X + ds + S(6), tr.Y, tr.Width - ds - S(6), tr.Height);
                }
                TextRenderer.DrawText(g, d.Title, Font, tr, act ? Text_ : TextDim,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

                bool dirty = d.Exited;   // il programma della scheda è terminato
                if (act || hov)
                {
                    if (hov && hoverClose) using (var b = new SolidBrush(Hover)) g.FillEllipse(b, cr);
                    if (dirty && !(hov && hoverClose)) DrawDot(g, cr, Accent);
                    else DrawX(g, cr, act ? Text_ : TextDim);
                }
                else if (dirty) DrawDot(g, cr, TextDim);
                if (d.Busy)   // l'assistente sta lavorando
                    using (var p = new Pen(Accent, S(2))) g.DrawLine(p, r.Left + S(10), r.Bottom - S(2), r.Right - S(10), r.Bottom - S(2));
            }
            g.ResetClip();
            // schede nascoste a sinistra o a destra: il bordo sfuma, così si capisce che si può scorrere
            if (overflow)
            {
                int fw = S(24);
                if (scroll > 0)
                    using (var b = new LinearGradientBrush(new Rectangle(0, 0, fw, Height), TabBar, Color.FromArgb(0, TabBar), LinearGradientMode.Horizontal))
                        g.FillRectangle(b, 0, 0, fw, Height);
                if (scroll < scrollMax)
                    using (var b = new LinearGradientBrush(new Rectangle(clipRight - fw, 0, fw, Height), Color.FromArgb(0, TabBar), TabBar, LinearGradientMode.Horizontal))
                        g.FillRectangle(b, clipRight - fw, 0, fw, Height);
            }
            if (hoverPlus) using (var b = new SolidBrush(Hover)) g.FillRectangle(b, plusRect);
            using (var p = new Pen(TextDim, Math.Max(1f, scale * 1.3f)))
            {
                int cx = plusRect.Left + plusRect.Width / 2, cy = plusRect.Top + plusRect.Height / 2, h = S(6);
                g.DrawLine(p, cx - h, cy, cx + h, cy); g.DrawLine(p, cx, cy - h, cx, cy + h);
            }
            // freccia "nuova scheda con...": chevron dello stesso tratto del +
            if (hoverDrop) using (var b = new SolidBrush(Hover)) g.FillRectangle(b, dropRect);
            using (var p = new Pen(TextDim, Math.Max(1f, scale * 1.3f)))
            {
                int cx = dropRect.Left + dropRect.Width / 2, cy = dropRect.Top + dropRect.Height / 2, h = S(4);
                g.DrawLines(p, new[] { new Point(cx - h, cy - h / 2), new Point(cx, cy + h / 2), new Point(cx + h, cy - h / 2) });
            }
            if (overflow)
            {
                if (hoverList) using (var b = new SolidBrush(Hover)) g.FillRectangle(b, listRect);
                // elenco di tutte le schede: tre righe, diverso dalla freccia "nuova scheda con..."
                int cx = listRect.Left + listRect.Width / 2, cy = listRect.Top + listRect.Height / 2, h = S(6);
                using (var p = new Pen(TextDim, Math.Max(1f, scale * 1.3f)))
                    for (int k = -1; k <= 1; k++) g.DrawLine(p, cx - h, cy + k * S(4), cx + h, cy + k * S(4));
            }
        }

        void DrawX(Graphics g, Rectangle cr, Color c)
        {
            int h = S(4), cx = cr.Left + cr.Width / 2, cy = cr.Top + cr.Height / 2;
            using (var p = new Pen(c, Math.Max(1f, scale * 1.2f)))
            {
                g.DrawLine(p, cx - h, cy - h, cx + h, cy + h);
                g.DrawLine(p, cx - h, cy + h, cx + h, cy - h);
            }
        }

        void DrawDot(Graphics g, Rectangle cr, Color c)
        {
            int s = S(8);
            using (var b = new SolidBrush(c)) g.FillEllipse(b, cr.Left + (cr.Width - s) / 2, cr.Top + (cr.Height - s) / 2, s, s);
        }

        static GraphicsPath RoundTop(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
            p.CloseFigure();
            return p;
        }

        int HitTab(Point pt)
        {
            if (overflow && pt.X >= plusRect.Left) return -1;
            for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(pt)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Layout_();
            if (dragIndex >= 0 && e.Button == MouseButtons.Left)
            {
                if (!dragging && Math.Abs(e.X - dragStart.X) > S(6)) dragging = true;
                if (dragging)
                {
                    int target = HitTab(e.Location);
                    if (target >= 0 && target != dragIndex)
                    {
                        // Si scambia solo quando il puntatore supera metà della scheda vicina
                        var r = rects[target];
                        bool pass = target > dragIndex ? e.X > r.Left + r.Width / 2 : e.X < r.Left + r.Width / 2;
                        if (pass)
                        {
                            var d = Docs[dragIndex];
                            Docs.RemoveAt(dragIndex); Docs.Insert(target, d);
                            dragIndex = target; hoverIndex = target;
                            if (Reordered != null) Reordered();
                            Invalidate();
                        }
                    }
                    return;
                }
            }
            int hi = HitTab(e.Location);
            bool hc = hi >= 0 && CloseRect(rects[hi]).Contains(e.Location);
            bool hp = plusRect.Contains(e.Location), hl = overflow && listRect.Contains(e.Location), hd = dropRect.Contains(e.Location);
            if (hi != hoverIndex || hc != hoverClose || hp != hoverPlus || hl != hoverList || hd != hoverDrop)
            {
                hoverIndex = hi; hoverClose = hc; hoverPlus = hp; hoverList = hl; hoverDrop = hd;
                Invalidate();
            }
            int tk = hi >= 0 ? hi : hp ? -3 : hd ? -4 : -1;
            if (tk != tipIndex)
            {
                tipIndex = tk;
                string t = hi >= 0 ? Docs[hi].Tooltip : hp ? L.T("Nuova scheda (Ctrl+Maiusc+T)", "New tab (Ctrl+Shift+T)") : hd ? L.T("Nuova scheda con...", "New tab with...") : null;
                tip.SetToolTip(this, t);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverIndex = -1; hoverClose = hoverPlus = hoverList = hoverDrop = false; tipIndex = -2;
            tip.SetToolTip(this, null);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Layout_();
            int i = HitTab(e.Location);
            if (e.Button == MouseButtons.Left)
            {
                if (i >= 0 && !CloseRect(rects[i]).Contains(e.Location))
                {
                    // secondo clic di un doppio clic sul nome: rinomina
                    if (e.Clicks == 2) { dragIndex = -1; if (TabRenameRequested != null) TabRenameRequested(Docs[i]); return; }
                    dragIndex = i; dragStart = e.Location; dragging = false;
                    if (TabSelected != null) TabSelected(Docs[i]);
                }
            }
            else if (e.Button == MouseButtons.Right && i >= 0 && TabContextMenu != null)
                TabContextMenu(Docs[i], e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            Layout_();
            int i = HitTab(e.Location);
            if (e.Button == MouseButtons.Left)
            {
                bool wasDrag = dragging;
                dragIndex = -1; dragging = false;
                if (wasDrag) return;
                if (i >= 0 && CloseRect(rects[i]).Contains(e.Location)) { if (TabCloseRequested != null) TabCloseRequested(Docs[i]); }
                else if (plusRect.Contains(e.Location)) { if (NewRequested != null) NewRequested(); }
                else if (dropRect.Contains(e.Location)) { if (NewMenuRequested != null) NewMenuRequested(new Point(dropRect.Left, dropRect.Bottom)); }
                else if (overflow && listRect.Contains(e.Location)) { if (ListRequested != null) ListRequested(); }
            }
            else if (e.Button == MouseButtons.Middle && i >= 0 && TabCloseRequested != null) TabCloseRequested(Docs[i]);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            Layout_();

            if (e.Button == MouseButtons.Left && HitTab(e.Location) < 0 && !plusRect.Contains(e.Location) && !dropRect.Contains(e.Location) && !listRect.Contains(e.Location) && NewRequested != null)
                NewRequested();
        }

        // Rotellina e touchpad: i passi piccoli del touchpad (meno di 120) spostano di poco, non vengono scartati
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollBy(-e.Delta);
        }

        void ScrollBy(int delta)
        {
            if (!overflow) return;
            scroll += delta * S(80) / 120;
            Invalidate();
        }

        // scorrimento orizzontale del touchpad (due dita a destra e sinistra) e rotellina inclinabile
        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEHWHEEL = 0x020E;
            if (m.Msg == WM_MOUSEHWHEEL)
            {
                ScrollBy((short)((long)m.WParam >> 16));
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        // Rettangolo della scheda nella barra (vuoto se non c'è)
        public Rectangle TabBounds(TermTab d)
        {
            Layout_();
            int i = Docs.IndexOf(d);
            return i >= 0 && i < rects.Count ? rects[i] : Rectangle.Empty;
        }

        public Point ListMenuLocation { get { return new Point(listRect.Right, listRect.Bottom); } }
    }
}
