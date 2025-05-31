using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;

class FastImageRecreator : Form {
    static Random rand = new Random();
    static Bitmap original;
    static Bitmap canvas;
    static int[,] currentDiffMap;
    static long currentTotalDiff;

    PictureBox pictureBox;
    Label statusLabel;

    int shapeCount = 5000;
    int candidateCount = 250;
    int mutationCount = 300;
    int sampleStep = 3;
    const int Alpha = 190;

    int minShapeSize = 2;
    int maxShapeSize = 100;

    public FastImageRecreator() {
        original = new Bitmap(@"C:\Users\decod\Downloads\james-cat.png");
        canvas = new Bitmap(original.Width, original.Height, PixelFormat.Format24bppRgb);

        // Fill background with average color
        Color bgColor = GetAverageColorRegion(null, new Rectangle(0, 0, original.Width, original.Height), 1);
        using (Graphics g = Graphics.FromImage(canvas))
            g.Clear(bgColor);

        BuildDiffMap();

        Width = original.Width + 40;
        Height = original.Height + 80;
        pictureBox = new PictureBox {
            Image = (Bitmap)canvas.Clone(),
            Width = original.Width,
            Height = original.Height,
            Location = new Point(10, 10),
            SizeMode = PictureBoxSizeMode.Zoom
        };
        statusLabel = new Label {
            Location = new Point(10, original.Height + 20),
            Width = Width - 40,
            AutoSize = true
        };
        Controls.Add(pictureBox);
        Controls.Add(statusLabel);
        Shown += (s, e) => RunGeneration();
    }

    async void RunGeneration() {
        long maxDiff = original.Width * original.Height * 3L * 255;
        double lastAccuracy = 0.0;

        for (int i = 0; i < shapeCount; i++) {
            Rectangle bestBounds = Rectangle.Empty;
            GraphicsPath bestPath = null;
            Color bestColor = Color.Empty;
            long bestDiff = long.MaxValue;

            for (int c = 0; c < candidateCount; c++) {
                GraphicsPath path = CreateRandomShape(original.Width, original.Height, out Rectangle bounds);
                bounds.Intersect(new Rectangle(0, 0, original.Width, original.Height));
                if (bounds.Width <= 0 || bounds.Height <= 0) {
                    path.Dispose();
                    continue;
                }

                Color avgColor = GetAverageColorRegion(path, bounds, sampleStep);
                long regionOld = SumDiffRegion(bounds, sampleStep);
                long regionNew = ComputeRegionDiff(path, bounds, avgColor, sampleStep);
                long candidateDiff = currentTotalDiff - regionOld + regionNew;

                if (candidateDiff < bestDiff) {
                    bestPath?.Dispose();
                    bestDiff = candidateDiff;
                    bestBounds = bounds;
                    bestPath = path;
                    bestColor = avgColor;
                } else {
                    path.Dispose();
                }
            }

            // Try mutations on the best candidate
            for (int m = 0; m < mutationCount; m++) {
                GraphicsPath mutated = MutateShape(bestPath, original.Width, original.Height, out Rectangle mutatedBounds);
                mutatedBounds.Intersect(new Rectangle(0, 0, original.Width, original.Height));
                if (mutatedBounds.Width <= 0 || mutatedBounds.Height <= 0) {
                    mutated.Dispose();
                    continue;
                }

                Color avgColor = GetAverageColorRegion(mutated, mutatedBounds, sampleStep);
                long regionOld = SumDiffRegion(mutatedBounds, sampleStep);
                long regionNew = ComputeRegionDiff(mutated, mutatedBounds, avgColor, sampleStep);
                long mutatedDiff = currentTotalDiff - regionOld + regionNew;

                if (mutatedDiff < bestDiff) {
                    bestPath?.Dispose();
                    bestDiff = mutatedDiff;
                    bestBounds = mutatedBounds;
                    bestPath = mutated;
                    bestColor = avgColor;
                } else {
                    mutated.Dispose();
                }
            }

            double newAccuracy = 100.0 * (1.0 - (double)bestDiff / maxDiff);
            if (bestPath != null && bestDiff < currentTotalDiff) {
                using (Graphics g = Graphics.FromImage(canvas))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(Alpha, bestColor)))
                    g.FillPath(brush, bestPath);

                currentTotalDiff = UpdateDiffMapAlpha(bestBounds, bestPath, bestColor);
                bestPath.Dispose();
            }

            if (newAccuracy - lastAccuracy < 0.0001 && maxShapeSize > 6) {
                maxShapeSize = Math.Max(minShapeSize + 1, maxShapeSize - 1);
                Console.WriteLine($"Reduced max shape size to {maxShapeSize} due to small accuracy gain");
            }

            lastAccuracy = newAccuracy;

            pictureBox.Image?.Dispose();
            pictureBox.Image = (Bitmap)canvas.Clone();
            statusLabel.Text = $"{i + 1}/{shapeCount} - Accuracy: {newAccuracy:0.00}% (Size: {minShapeSize}-{maxShapeSize})";
            Application.DoEvents();
        }

        canvas.Save("output.jpg", ImageFormat.Jpeg);
        MessageBox.Show("Done!");
    }

    void BuildDiffMap() {
        currentDiffMap = new int[original.Width, original.Height];
        currentTotalDiff = 0;

        for (int y = 0; y < original.Height; y += sampleStep) {
            for (int x = 0; x < original.Width; x += sampleStep) {
                Color o = original.GetPixel(x, y);
                Color c = canvas.GetPixel(x, y);
                int diff = Math.Abs(o.R - c.R) + Math.Abs(o.G - c.G) + Math.Abs(o.B - c.B);
                currentDiffMap[x, y] = diff;
                currentTotalDiff += diff;
            }
        }
    }

    long SumDiffRegion(Rectangle bounds, int step) {
        long sum = 0;
        for (int y = bounds.Top; y < bounds.Bottom; y += step)
            for (int x = bounds.Left; x < bounds.Right; x += step)
                sum += currentDiffMap[x, y];
        return sum;
    }

    long ComputeRegionDiff(GraphicsPath path, Rectangle bounds, Color color, int step) {
        long sum = 0;
        using (Bitmap temp = new Bitmap(bounds.Width, bounds.Height)) {
            using (Graphics g = Graphics.FromImage(temp)) {
                g.Clear(Color.Transparent);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(Alpha, color))) {
                    Matrix transform = new Matrix();
                    transform.Translate(-bounds.Left, -bounds.Top);
                    path.Transform(transform);
                    g.FillPath(b, path);
                    transform.Invert();
                    path.Transform(transform);
                }
            }

            for (int y = 0; y < bounds.Height; y += step) {
                for (int x = 0; x < bounds.Width; x += step) {
                    Color oc = original.GetPixel(x + bounds.Left, y + bounds.Top);
                    Color nc = BlendColors(canvas.GetPixel(x + bounds.Left, y + bounds.Top), temp.GetPixel(x, y));
                    sum += Math.Abs(oc.R - nc.R) + Math.Abs(oc.G - nc.G) + Math.Abs(oc.B - nc.B);
                }
            }
        }
        return sum;
    }

    long UpdateDiffMapAlpha(Rectangle bounds, GraphicsPath path, Color color) {
        long newTotal = currentTotalDiff;
        using (Bitmap temp = new Bitmap(bounds.Width, bounds.Height)) {
            using (Graphics g = Graphics.FromImage(temp)) {
                g.Clear(Color.Transparent);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(Alpha, color))) {
                    Matrix transform = new Matrix();
                    transform.Translate(-bounds.Left, -bounds.Top);
                    path.Transform(transform);
                    g.FillPath(b, path);
                    transform.Invert();
                    path.Transform(transform);
                }
            }

            for (int y = 0; y < bounds.Height; y += sampleStep) {
                for (int x = 0; x < bounds.Width; x += sampleStep) {
                    int cx = x + bounds.Left;
                    int cy = y + bounds.Top;
                    if (cx >= canvas.Width || cy >= canvas.Height) continue;

                    Color o = original.GetPixel(cx, cy);
                    Color c = canvas.GetPixel(cx, cy);
                    Color n = BlendColors(c, temp.GetPixel(x, y));

                    int oldDiff = currentDiffMap[cx, cy];
                    int newDiff = Math.Abs(o.R - n.R) + Math.Abs(o.G - n.G) + Math.Abs(o.B - n.B);
                    currentDiffMap[cx, cy] = newDiff;
                    newTotal += newDiff - oldDiff;
                }
            }
        }
        return newTotal;
    }

    GraphicsPath MutateShape(GraphicsPath original, int width, int height, out Rectangle bounds) {
        Matrix transform = new Matrix();
        float dx = (float)(rand.NextDouble() * 4 - 2); // shift -2 to +2 px
        float dy = (float)(rand.NextDouble() * 4 - 2);
        float scale = 1f + (float)(rand.NextDouble() * 0.1 - 0.05f); // scale ±5%
        float angle = (float)(rand.NextDouble() * 10 - 5); // rotate ±5 deg

        transform.Translate(dx, dy);
        transform.RotateAt(angle, new PointF(width / 2f, height / 2f));
        transform.Scale(scale, scale);

        GraphicsPath clone = (GraphicsPath)original.Clone();
        clone.Transform(transform);
        bounds = Rectangle.Round(clone.GetBounds());

        return clone;
    }

    Color GetAverageColorRegion(GraphicsPath path, Rectangle bounds, int step) {
        long r = 0, g = 0, b = 0;
        int count = 0;
        Region region = path == null ? null : new Region(path);

        for (int y = bounds.Top; y < bounds.Bottom; y += step) {
            for (int x = bounds.Left; x < bounds.Right; x += step) {
                if (x >= original.Width || y >= original.Height) continue;
                if (region != null && !region.IsVisible(x, y)) continue;
                Color c = original.GetPixel(x, y);
                r += c.R;
                g += c.G;
                b += c.B;
                count++;
            }
        }

        if (count == 0) {
            // Return the color of the first pixel in bounds if no valid pixels are found  
            int px = Math.Clamp(bounds.Left, 0, original.Width - 1);
            int py = Math.Clamp(bounds.Top, 0, original.Height - 1);
            return original.GetPixel(px, py);
        }

        return Color.FromArgb((int)(r / count), (int)(g / count), (int)(b / count));
    }

    GraphicsPath CreateRandomShape(int width, int height, out Rectangle bounds) {
        int shapeType = rand.Next(3);
        int w = rand.Next(minShapeSize, maxShapeSize);
        int h = rand.Next(minShapeSize, maxShapeSize);
        int x = rand.Next(width - w);
        int y = rand.Next(height - h);
        int cx = x + w / 2;
        int cy = y + h / 2;

        GraphicsPath path = new GraphicsPath();
        switch (shapeType) {
            case 0: path.AddEllipse(x, y, w, h); break;
            case 1:
                Point[] triangle = new Point[]
                {
                    new Point(x + rand.Next(w), y),
                    new Point(x, y + rand.Next(h)),
                    new Point(x + w, y + h)
                };
                path.AddPolygon(triangle);
                break;
            default:
                path.AddRectangle(new Rectangle(x, y, w, h));
                break;
        }

        Matrix rotate = new Matrix();
        rotate.RotateAt(rand.Next(360), new PointF(cx, cy));
        path.Transform(rotate);
        bounds = Rectangle.Round(path.GetBounds());
        return path;
    }

    static Color BlendColors(Color bg, Color fg) {
        float a = fg.A / 255f;
        int r = (int)(fg.R * a + bg.R * (1 - a));
        int g = (int)(fg.G * a + bg.G * (1 - a));
        int b = (int)(fg.B * a + bg.B * (1 - a));
        return Color.FromArgb(r, g, b);
    }

    [STAThread]
    static void Main() => Application.Run(new FastImageRecreator());
}
