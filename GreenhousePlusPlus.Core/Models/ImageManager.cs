﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog.Web;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace GreenhousePlusPlus.Core.Models
{
  /// <summary>
  /// Handles the file management of the images:
  /// paths, filenames, folder creation and file deletion.
  /// </summary>
  public class ImageManager
  {
    public const string ImageDir = "Images";

    private readonly string _imageRoot;
    
    private string BuildPath(string subDir) => Path.Combine(_imageRoot, ImageDir, subDir);
    public string BasePath => Path.Combine(_imageRoot, ImageDir);
    public string ImagePath => BuildPath("Original");
    public string ThumbsPath => Path.Combine(_imageRoot, ImageDir, "Thumbs");
    public string FilteredPath => Path.Combine(_imageRoot, ImageDir, "Filtered");
    public string SegmentedPath => Path.Combine(_imageRoot, ImageDir, "Segmented");
    public string HistPath => Path.Combine(_imageRoot, ImageDir, "Hist");
    public string KernelPath => Path.Combine(_imageRoot, ImageDir, "Kernels");
    public string PipelinePath => Path.Combine(_imageRoot, ImageDir, "Pipeline");

    public string Filename;
    public ImageFile Original;
    public ImageFile Thumb;
    public ImageFile FilteredGreen;
    public ImageFile FilteredRed;

    public ImageFile HistR;
    public ImageFile HistG;
    public ImageFile HistB;

    public ImageFile Leaf;
    public ImageFile Earth;
    public ImageFile Edge;
    public ImageFile PlantTip;
    public ImageFile Blur;
    public ImageFile HInterpolate;
    public ImageFile Pass;

    public ImageFile WholePipeline;

    private static bool _created = false;

    private bool _fileOpened = false;

    private readonly NLog.Logger _logger = NLogBuilder.ConfigureNLog("nlog.config").GetCurrentClassLogger();

    private void MkDir(string name)
    {
      if (_created)
      {
        return;
      }

      try
      {
        _logger.Info($"Creating directory: {name}");
        Directory.CreateDirectory(name);
      }
      catch (Exception ex)
      {
        _logger.Error(ex.Message);
        throw ex;
      }
    }

    public ImageManager(string imageRoot)
    {
      _imageRoot = imageRoot;
      MkDir(BasePath);
      MkDir(ImagePath);
      MkDir(ThumbsPath);
      MkDir(FilteredPath);
      MkDir(HistPath);
      MkDir(SegmentedPath);
      MkDir(KernelPath);
      MkDir(PipelinePath);
      _created = true;
    }

    public void Create(string srcFile)
    {
      var filename = Guid.NewGuid();
      Filename = filename + ".jpg";
      var destFile = Path.Combine(ImagePath, Filename);
      var thumbFile = Path.Combine(ThumbsPath, Filename);

      using (var image = Image.Load<Rgba32>(srcFile))
      {
        using (var target = image.Clone(x => x.Resize(0, 480)))
        {
          target.Save(destFile, new JpegEncoder {Quality = 80});
        }

        using (var thumb = image.Clone(x => x.Resize(0, 200)))
        {
          thumb.Save(thumbFile);
        }
      }

      Open(Filename);
    }

    public void Open(string filename)
    {
      Original = new ImageFile(_imageRoot, Path.Combine(ImagePath, filename));
      Thumb = new ImageFile(_imageRoot, Path.Combine(ThumbsPath, filename));

      HistR = new ImageFile(_imageRoot, Path.Combine(HistPath, "r_" + filename));
      HistG = new ImageFile(_imageRoot, Path.Combine(HistPath, "g_" + filename));
      HistB = new ImageFile(_imageRoot, Path.Combine(HistPath, "b_" + filename));

      var pngFilename = Path.GetFileNameWithoutExtension(filename) + ".png";

      FilteredGreen = new ImageFile(_imageRoot, Path.Combine(FilteredPath, "green_" + pngFilename));
      FilteredRed = new ImageFile(_imageRoot, Path.Combine(FilteredPath, "red_" + pngFilename));
      Earth = new ImageFile(_imageRoot, Path.Combine(SegmentedPath, "earth_" + pngFilename));
      Leaf = new ImageFile(_imageRoot, Path.Combine(SegmentedPath, "leaf_" + pngFilename));

      Edge = new ImageFile(_imageRoot, Path.Combine(KernelPath, "edge_" + pngFilename));
      PlantTip = new ImageFile(_imageRoot, Path.Combine(KernelPath, "edge_overlay" + pngFilename));
      Blur = new ImageFile(_imageRoot, Path.Combine(KernelPath, "blur_" + pngFilename));
      Pass = new ImageFile(_imageRoot, Path.Combine(KernelPath, "pass_" + pngFilename));
      HInterpolate = new ImageFile(_imageRoot, Path.Combine(KernelPath, "hinterpolate_" + pngFilename));

      WholePipeline = new ImageFile(_imageRoot, Path.Combine(PipelinePath, pngFilename));

      _fileOpened = true;
    }

    public IEnumerable<string> GetRelativeFilePaths()
    {
      if (Directory.Exists(BasePath))
      {
        return Directory.EnumerateFiles(ThumbsPath, "*.*", SearchOption.TopDirectoryOnly)
          // Remove full path and skip leading slash
          .Select(path => path.Replace(_imageRoot, ""))
          .Where(s => s.EndsWith(".jpg") || s.EndsWith(".png"));
      }

      return new List<string>();
    }

    public void Delete()
    {
      if (!_fileOpened || !File.Exists(Original.Path))
      {
        throw new FileNotFoundException("No file to delete");
      }

      Original.Delete();
      Thumb.Delete();
      FilteredGreen.Delete();
      FilteredRed.Delete();
      HistR.Delete();
      HistG.Delete();
      HistB.Delete();
      Earth.Delete();
      Leaf.Delete();
      Edge.Delete();
      PlantTip.Delete();
      Blur.Delete();
      Pass.Delete();
      HInterpolate.Delete();
      WholePipeline.Delete();
    }
  }
}