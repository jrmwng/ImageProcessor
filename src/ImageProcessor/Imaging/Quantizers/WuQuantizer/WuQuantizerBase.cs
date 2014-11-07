﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="WuQuantizerBase.cs" company="James South">
//   Copyright (c) James South.
//   Licensed under the Apache License, Version 2.0.
// </copyright>
// <summary>
//   Encapsulates methods to calculate the color palette of an image using
//   a Wu color quantizer <see href="http://www.ece.mcmaster.ca/~xwu/cq.c" />.
//   Adapted from <see href="https://github.com/drewnoakes" />
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace ImageProcessor.Imaging.Quantizers.WuQuantizer
{
    using System;
    using System.Drawing;
    using System.Linq;

    /// <summary>
    /// Encapsulates methods to calculate the color palette of an image using 
    /// a Wu color quantizer <see href="http://www.ece.mcmaster.ca/~xwu/cq.c"/>.
    /// Adapted from <see href="https://github.com/drewnoakes"/>
    /// </summary>
    public abstract class WuQuantizerBase
    {
        /// <summary>
        /// The alpha color component.
        /// </summary>
        protected const byte AlphaColor = 255;

        /// <summary>
        /// The position of the alpha component within a byte array.
        /// </summary>
        protected const int Alpha = 3;

        /// <summary>
        /// The position of the red component within a byte array.
        /// </summary>
        protected const int Red = 2;

        /// <summary>
        /// The position of the green component within a byte array.
        /// </summary>
        protected const int Green = 1;

        /// <summary>
        /// The position of the blue component within a byte array.
        /// </summary>
        protected const int Blue = 0;

        /// <summary>
        /// The size of a color cube side.
        /// </summary>
        private const int SideSize = 33;

        /// <summary>
        /// The maximum index within a color cube side
        /// </summary>
        private const int MaxSideIndex = 32;

        /// <summary>
        /// Quantize an image and return the resulting output bitmap
        /// </summary>
        /// <param name="source">
        /// The 32 bit per pixel image to quantize.
        /// </param>
        /// <returns>A quantized version of the image.</returns>
        public Image QuantizeImage(Bitmap source)
        {
            return this.QuantizeImage(source, 0, 1);
        }

        /// <summary>
        /// Quantize an image and return the resulting output bitmap
        /// </summary>
        /// <param name="source">
        /// The 32 bit per pixel image to quantize.
        /// </param>
        /// <param name="alphaThreshold">All colors with an alpha value less than this will be considered fully transparent.</param>
        /// <param name="alphaFader">Alpha values will be normalized to the nearest multiple of this value.</param>
        /// <returns>A quantized version of the image.</returns>
        public Image QuantizeImage(Bitmap source, int alphaThreshold, int alphaFader)
        {
            return this.QuantizeImage(source, alphaThreshold, alphaFader, null, 256);
        }

        /// <summary>
        /// Quantize an image and return the resulting output bitmap
        /// </summary>
        /// <param name="source">
        /// The 32 bit per pixel image to quantize.
        /// </param>
        /// <param name="alphaThreshold">
        /// All colors with an alpha value less than this will be considered fully transparent.
        /// </param>
        /// <param name="alphaFader">
        /// Alpha values will be normalized to the nearest multiple of this value.
        /// </param>
        /// <param name="histogram">
        /// The <see cref="Histogram"/> representing the distribution of color data.
        /// </param>
        /// <param name="maxColors">
        /// The maximum number of colors apply to the image.
        /// </param>
        /// <returns>
        /// A quantized version of the image.
        /// </returns>
        public Image QuantizeImage(Bitmap source, int alphaThreshold, int alphaFader, Histogram histogram, int maxColors)
        {
            ImageBuffer buffer = new ImageBuffer(source);

            if (histogram == null)
            {
                histogram = new Histogram();
            }
            else
            {
                histogram.Clear();
            }

            BuildHistogram(histogram, buffer, alphaThreshold, alphaFader);
            CalculateMoments(histogram.Moments);
            Box[] cubes = SplitData(ref maxColors, histogram.Moments);
            Pixel[] lookups = BuildLookups(cubes, histogram.Moments);
            return this.GetQuantizedImage(buffer, maxColors, lookups, alphaThreshold);
        }

        /// <summary>
        /// Builds a histogram from the current image.
        /// </summary>
        /// <param name="histogram">
        /// The <see cref="Histogram"/> representing the distribution of color data.
        /// </param>
        /// <param name="imageBuffer">
        /// The <see cref="ImageBuffer"/> for storing pixel information.
        /// </param>
        /// <param name="alphaThreshold">
        /// All colors with an alpha value less than this will be considered fully transparent.
        /// </param>
        /// <param name="alphaFader">
        /// Alpha values will be normalized to the nearest multiple of this value.
        /// </param>
        private static void BuildHistogram(Histogram histogram, ImageBuffer imageBuffer, int alphaThreshold, int alphaFader)
        {
            ColorMoment[, , ,] moments = histogram.Moments;

            foreach (Pixel[] pixelLine in imageBuffer.PixelLines)
            {
                foreach (Pixel pixel in pixelLine)
                {
                    byte pixelAlpha = pixel.Alpha;
                    if (pixelAlpha > alphaThreshold)
                    {
                        if (pixelAlpha < 255)
                        {
                            int alpha = pixel.Alpha + (pixel.Alpha % alphaFader);
                            pixelAlpha = (byte)(alpha > 255 ? 255 : alpha);
                        }

                        byte pixelRed = pixel.Red;
                        byte pixelGreen = pixel.Green;
                        byte pixelBlue = pixel.Blue;

                        pixelAlpha = (byte)((pixelAlpha >> 3) + 1);
                        pixelRed = (byte)((pixelRed >> 3) + 1);
                        pixelGreen = (byte)((pixelGreen >> 3) + 1);
                        pixelBlue = (byte)((pixelBlue >> 3) + 1);
                        moments[pixelAlpha, pixelRed, pixelGreen, pixelBlue].Add(pixel);
                    }
                }
            }

            // Set a default pixel for images with less than 256 colors.
            moments[0, 0, 0, 0].Add(new Pixel(0, 0, 0, 0));
        }

        private static void CalculateMoments(ColorMoment[, , ,] moments)
        {
            ColorMoment[,] xarea = new ColorMoment[SideSize, SideSize];
            ColorMoment[] area = new ColorMoment[SideSize];
            for (int alphaIndex = 1; alphaIndex < SideSize; alphaIndex++)
            {
                for (int redIndex = 1; redIndex < SideSize; redIndex++)
                {
                    Array.Clear(area, 0, area.Length);
                    for (int greenIndex = 1; greenIndex < SideSize; greenIndex++)
                    {
                        ColorMoment line = new ColorMoment();
                        for (int blueIndex = 1; blueIndex < SideSize; blueIndex++)
                        {
                            line.AddFast(ref moments[alphaIndex, redIndex, greenIndex, blueIndex]);
                            area[blueIndex].AddFast(ref line);
                            xarea[greenIndex, blueIndex].AddFast(ref area[blueIndex]);

                            ColorMoment moment = moments[alphaIndex - 1, redIndex, greenIndex, blueIndex];
                            moment.AddFast(ref xarea[greenIndex, blueIndex]);
                            moments[alphaIndex, redIndex, greenIndex, blueIndex] = moment;
                        }
                    }
                }
            }
        }

        private static ColorMoment Top(Box cube, int direction, int position, ColorMoment[, , ,] moment)
        {
            switch (direction)
            {
                case Alpha:
                    return (moment[position, cube.RedMaximum, cube.GreenMaximum, cube.BlueMaximum] -
                            moment[position, cube.RedMaximum, cube.GreenMinimum, cube.BlueMaximum] -
                            moment[position, cube.RedMinimum, cube.GreenMaximum, cube.BlueMaximum] +
                            moment[position, cube.RedMinimum, cube.GreenMinimum, cube.BlueMaximum]) -
                           (moment[position, cube.RedMaximum, cube.GreenMaximum, cube.BlueMinimum] -
                            moment[position, cube.RedMaximum, cube.GreenMinimum, cube.BlueMinimum] -
                            moment[position, cube.RedMinimum, cube.GreenMaximum, cube.BlueMinimum] +
                            moment[position, cube.RedMinimum, cube.GreenMinimum, cube.BlueMinimum]);

                case Red:
                    return (moment[cube.AlphaMaximum, position, cube.GreenMaximum, cube.BlueMaximum] -
                            moment[cube.AlphaMaximum, position, cube.GreenMinimum, cube.BlueMaximum] -
                            moment[cube.AlphaMinimum, position, cube.GreenMaximum, cube.BlueMaximum] +
                            moment[cube.AlphaMinimum, position, cube.GreenMinimum, cube.BlueMaximum]) -
                           (moment[cube.AlphaMaximum, position, cube.GreenMaximum, cube.BlueMinimum] -
                            moment[cube.AlphaMaximum, position, cube.GreenMinimum, cube.BlueMinimum] -
                            moment[cube.AlphaMinimum, position, cube.GreenMaximum, cube.BlueMinimum] +
                            moment[cube.AlphaMinimum, position, cube.GreenMinimum, cube.BlueMinimum]);

                case Green:
                    return (moment[cube.AlphaMaximum, cube.RedMaximum, position, cube.BlueMaximum] -
                            moment[cube.AlphaMaximum, cube.RedMinimum, position, cube.BlueMaximum] -
                            moment[cube.AlphaMinimum, cube.RedMaximum, position, cube.BlueMaximum] +
                            moment[cube.AlphaMinimum, cube.RedMinimum, position, cube.BlueMaximum]) -
                           (moment[cube.AlphaMaximum, cube.RedMaximum, position, cube.BlueMinimum] -
                            moment[cube.AlphaMaximum, cube.RedMinimum, position, cube.BlueMinimum] -
                            moment[cube.AlphaMinimum, cube.RedMaximum, position, cube.BlueMinimum] +
                            moment[cube.AlphaMinimum, cube.RedMinimum, position, cube.BlueMinimum]);

                case Blue:
                    return (moment[cube.AlphaMaximum, cube.RedMaximum, cube.GreenMaximum, position] -
                            moment[cube.AlphaMaximum, cube.RedMaximum, cube.GreenMinimum, position] -
                            moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMaximum, position] +
                            moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMinimum, position]) -
                           (moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMaximum, position] -
                            moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMinimum, position] -
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMaximum, position] +
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMinimum, position]);

                default:
                    return new ColorMoment();
            }
        }

        private static ColorMoment Bottom(Box cube, int direction, ColorMoment[, , ,] moment)
        {
            switch (direction)
            {
                case Alpha:
                    return (-moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMaximum, cube.BlueMaximum] +
                            moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMaximum] +
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMaximum] -
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMaximum]) -
                           (-moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMaximum, cube.BlueMinimum] +
                            moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMinimum] +
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMinimum] -
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMinimum]);

                case Red:
                    return (-moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMaximum] +
                            moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMaximum] +
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMaximum] -
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMaximum]) -
                           (-moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMinimum] +
                            moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMinimum] +
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMinimum] -
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMinimum]);

                case Green:
                    return (-moment[cube.AlphaMaximum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMaximum] +
                            moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMaximum] +
                            moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMaximum] -
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMaximum]) -
                           (-moment[cube.AlphaMaximum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMinimum] +
                            moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMinimum] +
                            moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMinimum] -
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMinimum]);

                case Blue:
                    return (-moment[cube.AlphaMaximum, cube.RedMaximum, cube.GreenMaximum, cube.BlueMinimum] +
                            moment[cube.AlphaMaximum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMinimum] +
                            moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMinimum] -
                            moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMinimum]) -
                           (-moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMaximum, cube.BlueMinimum] +
                            moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMinimum] +
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMinimum] -
                            moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMinimum]);

                default:
                    return new ColorMoment();
            }
        }

        private static CubeCut Maximize(ColorMoment[, , ,] moments, Box cube, int direction, byte first, byte last, ColorMoment whole)
        {
            var bottom = Bottom(cube, direction, moments);
            var result = 0.0f;
            byte? cutPoint = null;

            for (byte position = first; position < last; ++position)
            {
                ColorMoment half = bottom + Top(cube, direction, position, moments);
                if (half.Weight == 0)
                {
                    continue;
                }

                var temp = half.WeightedDistance();

                half = whole - half;
                if (half.Weight != 0)
                {
                    temp += half.WeightedDistance();

                    if (temp > result)
                    {
                        result = temp;
                        cutPoint = position;
                    }
                }
            }

            return new CubeCut(cutPoint, result);
        }

        private static bool Cut(ColorMoment[, , ,] moments, ref Box first, ref Box second)
        {
            int direction;
            var whole = Volume(first, moments);
            var maxAlpha = Maximize(moments, first, Alpha, (byte)(first.AlphaMinimum + 1), first.AlphaMaximum, whole);
            var maxRed = Maximize(moments, first, Red, (byte)(first.RedMinimum + 1), first.RedMaximum, whole);
            var maxGreen = Maximize(moments, first, Green, (byte)(first.GreenMinimum + 1), first.GreenMaximum, whole);
            var maxBlue = Maximize(moments, first, Blue, (byte)(first.BlueMinimum + 1), first.BlueMaximum, whole);

            if ((maxAlpha.Value >= maxRed.Value) && (maxAlpha.Value >= maxGreen.Value) && (maxAlpha.Value >= maxBlue.Value))
            {
                direction = Alpha;
                if (maxAlpha.Position == null) return false;
            }
            else if ((maxRed.Value >= maxAlpha.Value) && (maxRed.Value >= maxGreen.Value) && (maxRed.Value >= maxBlue.Value))
                direction = Red;
            else
            {
                if ((maxGreen.Value >= maxAlpha.Value) && (maxGreen.Value >= maxRed.Value) && (maxGreen.Value >= maxBlue.Value))
                    direction = Green;
                else
                    direction = Blue;
            }

            second.AlphaMaximum = first.AlphaMaximum;
            second.RedMaximum = first.RedMaximum;
            second.GreenMaximum = first.GreenMaximum;
            second.BlueMaximum = first.BlueMaximum;

            switch (direction)
            {
                case Alpha:
                    second.AlphaMinimum = first.AlphaMaximum = (byte)maxAlpha.Position;
                    second.RedMinimum = first.RedMinimum;
                    second.GreenMinimum = first.GreenMinimum;
                    second.BlueMinimum = first.BlueMinimum;
                    break;

                case Red:
                    second.RedMinimum = first.RedMaximum = (byte)maxRed.Position;
                    second.AlphaMinimum = first.AlphaMinimum;
                    second.GreenMinimum = first.GreenMinimum;
                    second.BlueMinimum = first.BlueMinimum;
                    break;

                case Green:
                    second.GreenMinimum = first.GreenMaximum = (byte)maxGreen.Position;
                    second.AlphaMinimum = first.AlphaMinimum;
                    second.RedMinimum = first.RedMinimum;
                    second.BlueMinimum = first.BlueMinimum;
                    break;

                case Blue:
                    second.BlueMinimum = first.BlueMaximum = (byte)maxBlue.Position;
                    second.AlphaMinimum = first.AlphaMinimum;
                    second.RedMinimum = first.RedMinimum;
                    second.GreenMinimum = first.GreenMinimum;
                    break;
            }

            first.Size = (first.AlphaMaximum - first.AlphaMinimum) * (first.RedMaximum - first.RedMinimum) * (first.GreenMaximum - first.GreenMinimum) * (first.BlueMaximum - first.BlueMinimum);
            second.Size = (second.AlphaMaximum - second.AlphaMinimum) * (second.RedMaximum - second.RedMinimum) * (second.GreenMaximum - second.GreenMinimum) * (second.BlueMaximum - second.BlueMinimum);

            return true;
        }

        private static float CalculateVariance(ColorMoment[, , ,] moments, Box cube)
        {
            ColorMoment volume = Volume(cube, moments);
            return volume.Variance();
        }

        private static ColorMoment Volume(Box cube, ColorMoment[, , ,] moment)
        {
            return (moment[cube.AlphaMaximum, cube.RedMaximum, cube.GreenMaximum, cube.BlueMaximum] -
                    moment[cube.AlphaMaximum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMaximum] -
                    moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMaximum] +
                    moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMaximum] -
                    moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMaximum, cube.BlueMaximum] +
                    moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMaximum] +
                    moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMaximum] -
                    moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMaximum]) -

                   (moment[cube.AlphaMaximum, cube.RedMaximum, cube.GreenMaximum, cube.BlueMinimum] -
                    moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMaximum, cube.BlueMinimum] -
                    moment[cube.AlphaMaximum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMinimum] +
                    moment[cube.AlphaMinimum, cube.RedMaximum, cube.GreenMinimum, cube.BlueMinimum] -
                    moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMinimum] +
                    moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMaximum, cube.BlueMinimum] +
                    moment[cube.AlphaMaximum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMinimum] -
                    moment[cube.AlphaMinimum, cube.RedMinimum, cube.GreenMinimum, cube.BlueMinimum]);
        }

        private static Box[] SplitData(ref int colorCount, ColorMoment[, , ,] moments)
        {
            --colorCount;
            var next = 0;
            var volumeVariance = new float[colorCount];
            var cubes = new Box[colorCount];
            cubes[0].AlphaMaximum = MaxSideIndex;
            cubes[0].RedMaximum = MaxSideIndex;
            cubes[0].GreenMaximum = MaxSideIndex;
            cubes[0].BlueMaximum = MaxSideIndex;
            for (var cubeIndex = 1; cubeIndex < colorCount; ++cubeIndex)
            {
                if (Cut(moments, ref cubes[next], ref cubes[cubeIndex]))
                {
                    volumeVariance[next] = cubes[next].Size > 1 ? CalculateVariance(moments, cubes[next]) : 0.0f;
                    volumeVariance[cubeIndex] = cubes[cubeIndex].Size > 1 ? CalculateVariance(moments, cubes[cubeIndex]) : 0.0f;
                }
                else
                {
                    volumeVariance[next] = 0.0f;
                    cubeIndex--;
                }

                next = 0;
                var temp = volumeVariance[0];

                for (var index = 1; index <= cubeIndex; ++index)
                {
                    if (volumeVariance[index] <= temp) continue;
                    temp = volumeVariance[index];
                    next = index;
                }

                if (temp > 0.0) continue;
                colorCount = cubeIndex + 1;
                break;
            }
            return cubes.Take(colorCount).ToArray();
        }

        private static Pixel[] BuildLookups(Box[] cubes, ColorMoment[, , ,] moments)
        {
            Pixel[] lookups = new Pixel[cubes.Length];

            for (int cubeIndex = 0; cubeIndex < cubes.Length; cubeIndex++)
            {
                ColorMoment volume = Volume(cubes[cubeIndex], moments);

                if (volume.Weight <= 0)
                {
                    continue;
                }

                Pixel lookup = new Pixel
                {
                    Alpha = (byte)(volume.Alpha / volume.Weight),
                    Red = (byte)(volume.Red / volume.Weight),
                    Green = (byte)(volume.Green / volume.Weight),
                    Blue = (byte)(volume.Blue / volume.Weight)
                };

                lookups[cubeIndex] = lookup;
            }

            return lookups;
        }

        internal abstract Image GetQuantizedImage(ImageBuffer image, int colorCount, Pixel[] lookups, int alphaThreshold);
    }
}