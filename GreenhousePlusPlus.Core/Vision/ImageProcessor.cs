﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GreenhousePlusPlus.Core.Models;
using Microsoft.VisualBasic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;

namespace GreenhousePlusPlus.Core.Vision
{
  public class ImageProcessor
  {
    private readonly ImageManager _imageFile;

    public ImageProcessor(ImageManager imageManager)
    {
      _imageFile = imageManager;
    }

    /// <summary>
    /// This method does three things:
    ///   1. Create a histogram or the original image color distribution
    ///   2. Creates a new image with filtered green colors
    ///   2. Creates a new image with filtered red colors
    /// </summary>
    /// <returns></returns>
    public ImageProcessResult Start(FilterValues thresholds)
    {
      var filterResult = Filter(thresholds, _imageFile.Original.Path);

      filterResult.RedImage.Save(_imageFile.FilteredRed.Path);
      filterResult.GreenImage.Save(_imageFile.FilteredGreen.Path);
      filterResult.LeafImage.Save(_imageFile.Leaf.Path);
      filterResult.EarthImage.Save(_imageFile.Earth.Path);
      filterResult.EdgeImage.Save(_imageFile.Edge.Path);
      filterResult.PlantTipImage.Save(_imageFile.PlantTip.Path);
      filterResult.BlurImage.Save(_imageFile.Blur.Path);
      filterResult.PassImage.Save(_imageFile.Pass.Path);
      filterResult.HInterpolateImage.Save(_imageFile.HInterpolate.Path);
      filterResult.Histogram.Draw(filterResult, false, 256, 200);
      filterResult.Histogram.HistogramR.Save(_imageFile.HistR.Path);
      filterResult.Histogram.HistogramG.Save(_imageFile.HistG.Path);
      filterResult.Histogram.HistogramB.Save(_imageFile.HistB.Path);
      filterResult.WholePipeline.Save(_imageFile.WholePipeline.Path);

      return new ImageProcessResult
      {
        FilterValues = thresholds,
        Files = new List<FilterFileInfo>()
        {
          new FilterFileInfo {Path = _imageFile.WholePipeline.RelativePath, Element = "pipeline"},
          new FilterFileInfo {Path = _imageFile.Original.RelativePath, Element = "original"},
          new FilterFileInfo {Path = _imageFile.Blur.RelativePath, Element = "blur"},
          new FilterFileInfo {Path = _imageFile.Earth.RelativePath, Element = "earth"},
          new FilterFileInfo {Path = _imageFile.Edge.RelativePath, Element = "edge"},
          new FilterFileInfo {Path = _imageFile.FilteredGreen.RelativePath, Element = "green"},
          new FilterFileInfo {Path = _imageFile.FilteredRed.RelativePath, Element = "red"},
          new FilterFileInfo {Path = _imageFile.PlantTip.RelativePath, Element = "tip"},
          new FilterFileInfo {Path = _imageFile.Leaf.RelativePath, Element = "leaf"},
          new FilterFileInfo {Path = _imageFile.HInterpolate.RelativePath, Element = "hinterpolate"},
          new FilterFileInfo {Path = _imageFile.HistR.RelativePath, Element = "hist-red"},
          new FilterFileInfo {Path = _imageFile.HistG.RelativePath, Element = "hist-green"},
          new FilterFileInfo {Path = _imageFile.HistB.RelativePath, Element = "hist-blue"},
          new FilterFileInfo {Path = _imageFile.Pass.RelativePath, Element = "pass"}
        }
      };
    }

    public static FilterResult Filter(FilterValues filters, string path)
    {
      var results = new List<Histogram>();
      var filterCount = 9;

      var destImages = new Image<Rgba32>[filterCount];
      var srcImage = Image.Load<Rgba32>(path);

      IImageInfo info;
      using (var file = new FileStream(path, FileMode.Open))
      {
        info = Image.Identify(file);
      }

      // Reserve image memory
      for (int i = 0; i < filterCount; i++)
      {
        destImages[i] = new Image<Rgba32>(info.Width, info.Height);
      }

      // Create threads
      var threads = Environment.ProcessorCount;
      double chunks = (double) info.Height / threads;

      Parallel.For(0, threads, i =>
      {
        // Leave a image edge of 1px for kernel
        var rect = new FilterRect
        {
          x = 1,
          y = (int) (i * chunks) + 1,
          endx = info.Width - 1,
          endy = (int) ((i + 1) * chunks) + (i == (threads - 1) ? -1 : +1)
        };
        results.Add(ApplyFilters(filters, srcImage, destImages, rect));
      });

      LongestVerticalLineCount(
        srcImage: srcImage,
        srcFilter: destImages[ImageIdx.Passfilter],
        hinterpolateImage: destImages[ImageIdx.HInterpolte],
        destImage: destImages[ImageIdx.LongestEdgeOverlay],
        avgWindow: filters.ScanlineInterpolationWidth);

      // Processing now completed

      // Copy all filtered images into one
      var stages = new Image<Rgba32>[]
      {
        destImages[ImageIdx.EdgeFilter],
        destImages[ImageIdx.Blur],
        destImages[ImageIdx.Passfilter],
        destImages[ImageIdx.HInterpolte],
        destImages[ImageIdx.LongestEdgeOverlay]
      };
      var pipelineImage = new Image<Rgba32>(stages.Length * info.Width, info.Height);

      for (int y = 0; y < info.Height; y++)
      {
        var targetRow = pipelineImage.GetPixelRowSpan(y);
        // Copy all stages rows into one giant column side by side
        for (int i = 0; i < stages.Length; i++)
        {
          var srcRow = stages[i].GetPixelRowSpan(y);
          for (int x = 0; x < info.Width; x++)
          {
            targetRow[x + i * info.Width] = srcRow[x];
          }
        }
      }

      return new FilterResult
      {
        Histogram = results.Aggregate(new Histogram(), (a, b) => a.Add(b)),
        RedImage = destImages[ImageIdx.Red],
        GreenImage = destImages[ImageIdx.Green],
        LeafImage = destImages[ImageIdx.Leaf],
        EarthImage = destImages[ImageIdx.Earth],
        EdgeImage = destImages[ImageIdx.EdgeFilter],
        PassImage = destImages[ImageIdx.Passfilter],
        BlurImage = destImages[ImageIdx.Blur],
        HInterpolateImage =  destImages[ImageIdx.HInterpolte],
        PlantTipImage = destImages[ImageIdx.LongestEdgeOverlay],
        WholePipeline = pipelineImage
      };
    }

    private static Histogram ApplyFilters(FilterValues filterValues, in Image<Rgba32> srcImage, Image<Rgba32>[] images,
      FilterRect rect)
    {
      // This is all one run for performance reasons because instead of n runs it needs just one
      var h = SobelFilterAndSegmentation(srcImage, images, filterValues, rect);

      VerticalBlur(
        srcImage: images[ImageIdx.EdgeFilter],
        destImage: images[ImageIdx.Blur],
        blurRounds: filterValues.BlurRounds,
        rect);

      Highpass(
        srcImage: images[ImageIdx.Blur],
        destImage: images[ImageIdx.Passfilter],
        whiteThreshold: filterValues.WhiteThreshold,
        rect);

      return h;
    }

    #region filters

    private static Histogram SobelFilterAndSegmentation(in Image<Rgba32> srcImage, Image<Rgba32>[] images,
      FilterValues filterValues, FilterRect rect)
    {
      var h = new Histogram();
      double epsilon = 1.0;

      for (int y = rect.y; y < rect.endy; y++)
      {
        // The sobel operator needs adjacent rows and cols of a pixel
        var topSpan = srcImage.GetPixelRowSpan(y - 1);
        var rowSpan = srcImage.GetPixelRowSpan(y);
        var bottomSpan = srcImage.GetPixelRowSpan(y + 1);

        var outputEdgeSpan = images[ImageIdx.EdgeFilter].GetPixelRowSpan(y);
        var outputRedSpan = images[ImageIdx.Red].GetPixelRowSpan(y);
        var outputGreenSpan = images[ImageIdx.Green].GetPixelRowSpan(y);
        var outputLeafSpan = images[ImageIdx.Leaf].GetPixelRowSpan(y);
        var outputEarthSpan = images[ImageIdx.Earth].GetPixelRowSpan(y);

        for (int x = rect.x; x < rect.endx; x++)
        {
          var r = rowSpan[x].R;
          var g = rowSpan[x].G;
          var b = rowSpan[x].B;

          // Sobel operator
          var pixel_x =
            // Horizontal difference between right and left pixel row adjacent pixels also
            // Row above
            topSpan[x + 1].R - topSpan[x - 1].R
            // Some row
            + (2 * rowSpan[x + 1].R - 2 * rowSpan[x - 1].R)
            // Row below
            + (bottomSpan[x + 1].R - bottomSpan[x - 1].R);

          var pixel_y =
            // Vertical difference between bottom and top row for all positions: x-1, x, x+1
            bottomSpan[x - 1].R - topSpan[x - 1].R
            // Some row
            + (2 * bottomSpan[x].R - 2 * topSpan[x].R)
            // Row below
            + (bottomSpan[x + 1].R - topSpan[x + 1].R);

          // Theta is 0 for vertical edges, but real world numbers...fine tune.
          var theta = Math.Atan2(pixel_y, pixel_x);
          if (theta > -filterValues.ThetaTheshold && theta < filterValues.ThetaTheshold)
          {
            var mag = Math.Ceiling(Math.Sqrt((pixel_x * pixel_x) + (pixel_y * pixel_y)));
            byte val = 0;
            if (mag < 60)
            {
              val = (byte) (255 - mag);
            }

            outputEdgeSpan[x] = new Rgba32(val, val, val);
          }
          else
          {
            outputEdgeSpan[x] = new Rgba32(255, 255, 255);
          }

          h.RGBArray.R[r]++;
          h.RGBArray.G[g]++;
          h.RGBArray.B[b]++;

          // epsion prevents div by zero
          double redRatio = (r + epsilon) / (Math.Max(g, b) + epsilon);
          double greenRatio = (g + epsilon) / (Math.Max(r, b) + epsilon);
          var redDominant = redRatio > filterValues.RedMinRatio;
          var greenDominant = greenRatio > filterValues.GreenMinRatio;

          // Set other color only blue
          if (!redDominant)
          {
            outputRedSpan[x] = Rgba32.Transparent;
          }
          else // Red is dominant
          {
            outputLeafSpan[x] = Rgba32.Transparent;
            outputRedSpan[x] = new Rgba32(130, 69, 10);
          }

          if (!greenDominant)
          {
            outputGreenSpan[x] = Rgba32.Transparent;
            outputEarthSpan[x] = new Rgba32(r, g, b);
          }
          else // Green is dominant
          {
            outputEarthSpan[x] = Rgba32.Transparent;
            outputGreenSpan[x] = new Rgba32(0, 255, 0);
            outputLeafSpan[x] = new Rgba32(r, g, b);
          }
        }
      }

      return h;
    }

    private static void VerticalBlur(in Image<Rgba32> srcImage, Image<Rgba32> destImage, int blurRounds, FilterRect rect)
    {
      var blurredImage = srcImage.Clone();
      for (int z = 0; z < blurRounds; z++)
      {
        for (int y = rect.y; y < rect.endy; y++)
        {
          var topSpan = blurredImage.GetPixelRowSpan(y - 1);
          var rowSpan = blurredImage.GetPixelRowSpan(y);
          var bottomSpan = blurredImage.GetPixelRowSpan(y + 1);
          var destRowSpan = destImage.GetPixelRowSpan(y);
          for (int x = rect.x; x < rect.endx; x++)
          {
            var blur = (byte) ((topSpan[x].R + rowSpan[x].R + bottomSpan[x].R) / 3.0);
            destRowSpan[x] = new Rgba32(blur, blur, blur, 255);
          }
        }

        blurredImage = destImage.Clone();
      }
    }

    private static void Highpass(in Image<Rgba32> srcImage, Image<Rgba32> destImage, int whiteThreshold, FilterRect rect)
    {
      for (int y = rect.y; y < rect.endy; y++)
      {
        var srcSpan = srcImage.GetPixelRowSpan(y);
        var destRowSpan = destImage.GetPixelRowSpan(y);
        for (int x = rect.x; x < rect.endx; x++)
        {
          var col = srcSpan[x].R;
          if (col > whiteThreshold)
          {
            col = 255;
          }

          destRowSpan[x] = new Rgba32(col, col, col, 255);
        }
      }
    }

    /// <summary>
    /// Scanline algorithm: Count vertical intersecting with horizontal line
    /// Interpolate horizontally for robustness.
    /// </summary>
    /// <param name="srcImage"></param>
    /// <param name="srcFilter"></param>
    /// <param name="destImage"></param>
    /// <param name="avgWindow"></param>
    private static void LongestVerticalLineCount(in Image<Rgba32> srcImage, in Image<Rgba32> hinterpolateImage,  in Image<Rgba32> srcFilter, Image<Rgba32> destImage, int avgWindow)
    {
      // Min value for neighbourhood average value
      double minAvgThreshold = 255.0 * 0.25;
      // Copy image
      // destImage = srcImage.Clone(); <--- Why doesnt this work?
      for (int y = 0; y < srcImage.Height; y++)
      {
        var srcRowSpan = srcImage.GetPixelRowSpan(y);
        var destRowSpan = destImage.GetPixelRowSpan(y);
        for (int x = 0; x < srcImage.Width; x++)
        {
          destRowSpan[x] = srcRowSpan[x];
        }
      }

      var w = srcImage.Width;
      var h = srcImage.Height;
      var vCount = new bool[w, h];
      int skipEdge = 5;

      for (int y = skipEdge; y < h - skipEdge; y++)
      {
        var hInterpolateRow = hinterpolateImage.GetPixelRowSpan(y);
        var srcFilterRow = srcFilter.GetPixelRowSpan(y);
        for (int x = avgWindow + skipEdge; x < w - avgWindow - skipEdge; x++)
        {
          double sum = 0.0;
          // Horizontal average / interpolation,accepts slight slopes
          for (int k = x - avgWindow; k <= x + avgWindow; k++)
          {
            // Remember, black is 0, to sum black pixel invert them
            sum += 255.0 - srcFilterRow[k].R;
          }

          double avg = sum / (2 * avgWindow + 1);

          // High pass filter: If the neighbourhood is slightly black then count it
          vCount[x, y] = avg > minAvgThreshold;
          hInterpolateRow[x] = vCount[x, y] ? Color.Black : Color.White;
        }
      }

      var maxCol = new ColCounter {X = 0, Y = 0, Length = 0};
      // Column-wise longest line
      for (int x = 0; x < w; x++)
      {
        int colSum = 0;
        for (int y = 0; y < h; y++)
        {
          if (vCount[x, y])
          {
            colSum++;
          }
          else
          {
            if (maxCol.Length < colSum)
            {
              maxCol.X = x;
              maxCol.Y = y - colSum;
              maxCol.Length = colSum;
            }

            colSum = 0;
          }
        }
      }

      // Mark on image the longest vertical line
      for (int y = maxCol.Y; y < maxCol.Length; y++)
      {
        var destRowSpan = destImage.GetPixelRowSpan(y);

        // Like thickness
        for (int x = maxCol.X - 2; x < maxCol.X + 2; x++)
        {
          var c = destImage[x, y];
          destRowSpan[x] = new Rgba32((float) 200 / 255, ((float) c.G / 2) / 255, ((float) c.B / 2) / 255);
        }

        // Horizontal line at the end of the line
        if (y > (maxCol.Length - 6))
        {
          for (int x = 0; x < w; x++)
          {
            var c = destImage[x, y];
            destRowSpan[x] = new Rgba32((float) 200 / 255, ((float) c.G / 2) / 255, ((float) c.B / 2) / 255);
          }
        }
      }
    }

    #endregion
  }
}