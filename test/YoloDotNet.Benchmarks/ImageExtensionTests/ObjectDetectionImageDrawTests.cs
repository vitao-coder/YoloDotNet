﻿namespace YoloDotNet.Benchmarks.ImageExtensionTests
{
    [MemoryDiagnoser]
    public class ObjectDetectionImageDrawTests
    {
        #region Fields

        private static string _model = SharedConfig.GetTestModel(modelType: ModelType.ObjectDetection);
        private static string _testImage = SharedConfig.GetTestImage(imageType: ImageType.Street);

        private Yolo _cpuYolo;
        private Image _image;
        private Stream _imageJpegDataStream;
        private List<ObjectDetection> _objectDetections;

        #endregion Fields

        #region Methods

        [GlobalSetup]
        public void GlobalSetup()
        {
            _cpuYolo = new Yolo(onnxModel: _model, cuda: false);
            _image = Image.Load(path: _testImage);
            _objectDetections = _cpuYolo.RunObjectDetection(img: _image, confidence: 0.25, iou: 0.45);
        }

        [IterationSetup]
        public void IterationSetup()
        {
            _imageJpegDataStream = new MemoryStream();
            _image.SaveAsJpeg(_imageJpegDataStream);
            _imageJpegDataStream.Position = 0;
        }

        [Params(true, false)]
        public bool DrawConfidence { get; set; }

        [Benchmark(Baseline = true)]
        public Image DrawObjectDetection()
        {
            _image.Draw(detections: _objectDetections, drawConfidence: DrawConfidence);

            return _image;
        }

        [Benchmark]
        public SKBitmap DrawExperimentalSkiaSharp()
        {
            SKBitmap bitmap = SKBitmap.Decode(_imageJpegDataStream);
            using (SKCanvas canvas = new SKCanvas(bitmap))
            {
                SKTypeface typeface = SKTypeface.FromFamilyName(
                    familyName: nameof(FontType.Arial),
                    weight: SKFontStyleWeight.Bold,
                    width: SKFontStyleWidth.Normal,
                    slant: SKFontStyleSlant.Upright);

                var textPainter = new SKPaint
                {
                    TextSize = 16,
                    IsAntialias = false,
                    Typeface = typeface,
                    Color = new SKColor(red: ImageConfig.FOREGROUND_COLOR.R, green: ImageConfig.FOREGROUND_COLOR.G, blue: ImageConfig.FOREGROUND_COLOR.B, alpha: ImageConfig.FOREGROUND_COLOR.A),
                    Style = SKPaintStyle.Fill
                };

                foreach (var detection in _objectDetections)
                {
                    var color = HexToRgba(detection.Label.Color, ImageConfig.DEFAULT_OPACITY);

                    var mainPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Stroke,
                        Color = new SKColor(red: color.R, green: color.G, blue: color.B, alpha: color.A),
                        StrokeWidth = 2
                    };

                    var textBackgroundPaint = new SKPaint
                    {
                        Style = SKPaintStyle.Fill,
                        Color = new SKColor(red: color.R, green: color.G, blue: color.B, alpha: color.A),
                        StrokeWidth = 2
                    };

                    // Text with label name and confidence in percent
                    var text = detection.Label.Name;

                    if (!DrawConfidence)
                        text += $" ({detection!.Confidence.ToPercent()}%)";

                    var boundingBoxRectangle = new SKRect(
                        left: detection.BoundingBox.Left,
                        top: detection.BoundingBox.Top,
                        right: detection.BoundingBox.Right,
                        bottom: detection.BoundingBox.Bottom);

                    var textRectangle = new SKRect();
                    var textWidth = textPainter.MeasureText(text, ref textRectangle);

                    canvas.DrawRect(boundingBoxRectangle, mainPaint);

                    var (x, y) = (detection.BoundingBox.X + 5, detection.BoundingBox.Y - (textRectangle.Height));

                    var textBackgroundRectangle = new SKRect(
                        left: detection.BoundingBox.Left,
                        top: detection.BoundingBox.Top - textRectangle.Height - 5,
                        right: detection.BoundingBox.Left + textRectangle.Width + 10,
                        bottom: detection.BoundingBox.Top);

                    canvas.DrawRect(textBackgroundRectangle, textBackgroundPaint);

                    canvas.DrawText(text, x, y + (textPainter.TextSize / 2), textPainter);
                }
            }

            return bitmap;
        }

        private static Rgba32 HexToRgba(string hexColor, int alpha = 255)
        {
            var hexValid = Color.TryParseHex(hexColor, out _);

            if (hexColor.Length != 7 || hexValid is false)
                throw new ArgumentException("Invalid hexadecimal color format.");

            if (alpha < 0 || alpha > 255)
                throw new ArgumentOutOfRangeException(nameof(alpha), "Alfa value must be between 0-255.");

            byte r = byte.Parse(hexColor.Substring(1, 2), NumberStyles.HexNumber);
            byte g = byte.Parse(hexColor.Substring(3, 2), NumberStyles.HexNumber);
            byte b = byte.Parse(hexColor.Substring(5, 2), NumberStyles.HexNumber);

            return new Rgba32(r, g, b, (byte)alpha);
        }

        #endregion Methods
    }
}
