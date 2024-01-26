﻿using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Globalization;
using System.Text;
using System.Linq;
using YoloDotNet.Enums;
using YoloDotNet.Models;
using System.Collections.Concurrent;

namespace YoloDotNet.Extensions
{
    public static class ImageExtension
    {
        /// <summary>
        /// Creates a resized clone of the input image with new width, height and padded borders to fit new size.
        /// </summary>
        /// <param name="image">The original image to be resized.</param>
        /// <param name="w">The desired width for the resized image.</param>
        /// <param name="h">The desired height for the resized image.</param>
        /// <returns>A new image with the specified dimensions.</returns>
        public static Image<Rgb24> ResizeImage(this Image image, int w, int h)
        {
            var options = new ResizeOptions
            {
                Size = new Size(w, h),
                Mode = ResizeMode.Pad,
                PadColor = new Color(new Rgb24(0, 0, 0))
            };

            return image.Clone(x => x.Resize(options)).CloneAs<Rgb24>();
        }

        /// <summary>
        /// Extracts pixel values from an RGB image and converts them into a tensor.
        /// </summary>
        /// <param name="img">The RGB image to extract pixel values from.</param>
        /// <returns>A tensor containing normalized pixel values extracted from the input image.</returns>
        public static Tensor<float> ExtractPixelsFromImage(this Image<Rgb24> img, int inputBatchSize, int inputChannels)
        {
            var (width, height) = (img.Width, img.Height);
            var tensor = new DenseTensor<float>(new[] { inputBatchSize, inputChannels, width, height });

            Parallel.For(0, height, y =>
            {
                var pixelSpan = img.DangerousGetPixelRowMemory(y).Span;

                for (int x = 0; x < width; x++)
                {
                    tensor[0, 0, y, x] = pixelSpan[x].R / 255.0F; // r
                    tensor[0, 1, y, x] = pixelSpan[x].G / 255.0F; // g
                    tensor[0, 2, y, x] = pixelSpan[x].B / 255.0F; // b
                }
            });

            return tensor;
        }

        /// <summary>
        /// Iterates over each pixel and invokes the provided method.
        /// </summary>
        public static Pixel[] GetPixels<TPixel>(this Image<TPixel> image, Func<TPixel, float> func) where TPixel : unmanaged, IPixel<TPixel>
        {
            var width = image.Width;
            var height = image.Height;

            var pixels = new ConcurrentBag<Pixel>();

            Parallel.For(0, height, y =>
            {
                var row = image.DangerousGetPixelRowMemory(y).Span;

                for (int x = 0; x < width; x++)
                {
                    var confidence = func(row[x]);

                    if (confidence > 0.75f)
                        pixels.Add(new Pixel(x, y, confidence));
                }
            });

            return pixels.ToArray();
        }

        /// <summary>
        /// Draws the specified segmentations on the image.
        /// </summary>
        public static void DrawSegmentation(this Image image, List<Segmentation> segmentations, bool drawConfidence = true)
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

            Parallel.ForEach(segmentations, options, segmentation =>
            {
                // Create a new transparent image and draw segmented pixel
                using var mask = new Image<Rgba32>(segmentation.Rectangle.Width, segmentation.Rectangle.Height);

                var color = Color.ParseHex(segmentation.Label.Color);

                foreach (var pixel in segmentation.SegmentedPixels)
                    mask[pixel.X, pixel.Y] = color;

                image.Mutate(x => x.DrawImage(mask, segmentation.Rectangle.Location, .28f));
            });

            BoundingBox(image, segmentations, drawConfidence);
        }

        public static void DrawBoundingBoxes(this Image image, IEnumerable<ObjectDetection> detections, bool drawConfidence = true)
        {
            BoundingBox(image, detections, drawConfidence);
        }

        private static void BoundingBox(Image image, IEnumerable<IDetection> detections, bool drawConfidence)
        {
            // Define constants for readability
            const int fontSize = 16;
            const int borderWidth = 2;
            const int shadowOffset = 1;

            // Define fonts and colors
            var font = SystemFonts.Get(nameof(FontType.Arial))
                .CreateFont(fontSize, FontStyle.Bold);

            var shadowColor = new Rgba32(44, 44, 44, 180);
            var foregroundColor = new Rgba32(248, 240, 227, 224);

            image.Mutate(context =>
            {
                foreach (var label in detections!)
                {
                    var labelColor = HexToRgba(label.Label.Color, 128);

                    // Text with label name and confidence in percent
                    var text = label.Label.Name;

                    if (drawConfidence)
                        text += $" ({label!.Confidence.ToPercent()}%)";

                    // Calculate text width and height
                    var textSize = TextMeasurer.MeasureSize(text, new TextOptions(font));

                    // Label x, y coordinates
                    var (x, y) = (label.Rectangle.X, label.Rectangle.Y - (textSize.Height * 2));

                    // Draw box
                    context.Draw(Pens.Solid(labelColor, borderWidth), label.Rectangle);

                    // Draw text background
                    context.Fill(labelColor, new RectangularPolygon(x, y, textSize.Width + fontSize, textSize.Height * 2));

                    // Draw text shadow
                    context.DrawText(text, font, shadowColor, new PointF(x + shadowOffset + (fontSize / 2), y + shadowOffset + (textSize.Height / 2)));

                    // Draw label text
                    context.DrawText(text, font, foregroundColor, new PointF(x + (fontSize / 2), y + (textSize.Height / 2)));
                }
            });
        }

        public static void DrawClassificationLabels(this Image image, IEnumerable<Classification>? labels, bool drawConfidence = true)
        {
            // Define constants for readability
            const int fontSize = 16;
            const int x = fontSize;
            const int y = fontSize;
            const int margin = fontSize / 2;
            const float lineSpace = 1.5f;

            // Define fonts and colors
            var font = GetFont(fontSize);
            var shadowColor = new Rgba32(0, 0, 0, 60);
            var foregroundColor = new Rgba32(255, 255, 255);

            var options = new RichTextOptions(font)
            {
                LineSpacing = lineSpace,
                Origin = new PointF(x + margin, y + margin)
            };

            // Gather labels and confidence score
            var sb = new StringBuilder();
            foreach (var label in labels!)
            {
                var text = label.Label;

                if (drawConfidence)
                    text += $" ({label!.Confidence.ToPercent()}%)";

                sb.AppendLine(text);
            }

            image.Mutate(context =>
            {
                // Calculate text width and height
                var textSize = TextMeasurer.MeasureSize(sb.ToString(), options);

                // Draw background
                context.Fill(shadowColor, new RectangularPolygon(x, y, textSize.Width + fontSize, textSize.Height + fontSize));

                // Draw labels
                context.DrawText(options, sb.ToString(), foregroundColor);
            });
        }

        /// <summary>
        /// Resize segmented image to original image size and crop selected area
        /// </summary>
        /// <param name="image"></param>
        /// <param name="cropRectangle"></param>
        /// <param name="resizeWidth"></param>
        /// <param name="resizeHeight"></param>
        public static void CropSegmentedArea(this Image image, Image originalImage, Rectangle rectangle)
        {
            var gain = Math.Min(image.Width / (float)originalImage.Width, image.Height / (float)originalImage.Height);

            var x = (int)((image.Width - originalImage.Width * gain) / 2);
            var y = (int)((image.Height - originalImage.Height * gain) / 2);
            var w = image.Width - x * 2;
            var h = image.Height - y * 2;

            image.Mutate(img =>
            {
                img.Crop(new Rectangle(x, y, w, h));
                img.Resize(originalImage.Width, originalImage.Height);
                img.Crop(rectangle);
            });
        }

        private static Font GetFont(int size)
            => SystemFonts.Get(nameof(FontType.Arial))
                .CreateFont(size, FontStyle.Bold);

        /// <summary>
        /// Converts a hexadecimal color representation to an Rgba32 color.
        /// </summary>
        /// <param name="hexColor">The hexadecimal color value (e.g., "#RRGGBB").</param>
        /// <param name="alpha">Optional. The alpha (transparency) value for the Rgba32 color (0-255, default is 255).</param>
        /// <returns>An Rgba32 color representation.</returns>
        /// <exception cref="ArgumentException">Thrown when the input hex color format is invalid.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the alpha value is outside the valid range (0-255).</exception>
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

    }
}
