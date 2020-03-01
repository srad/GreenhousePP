﻿using System;
using System.IO;

namespace Greenhouse.Models
{

  public class ImageManager
  {
    public static string BasePath = AppDomain.CurrentDomain.BaseDirectory + @"Images\";
    public static string ImagePath = AppDomain.CurrentDomain.BaseDirectory + @"Images\Original\";
    public static string ThumbsPath = AppDomain.CurrentDomain.BaseDirectory + @"Images\Thumbs\";
    public static string FilteredPath = AppDomain.CurrentDomain.BaseDirectory + @"Images\Filtered\";
    public static string HistPath = AppDomain.CurrentDomain.BaseDirectory + @"Images\Hist\";
    public static string SelectionPath = AppDomain.CurrentDomain.BaseDirectory + @"Images\Selection\";

    public readonly string Filename;
    public ImageFile Original;
    public ImageFile Thumb;
    public ImageFile FilteredGreen;
    public ImageFile FilteredRed;

    public ImageFile HistR;
    public ImageFile HistG;
    public ImageFile HistB;

    public ImageFile Selection;

    public ImageManager(string file)
    {
      Filename = file;

      Original = new ImageFile(ImagePath + file);
      Thumb = new ImageFile(ThumbsPath + file);

      FilteredGreen = new ImageFile(FilteredPath + "green_" + file);
      FilteredRed = new ImageFile(FilteredPath + "red_" + file);

      HistR = new ImageFile(HistPath + "r_" + file);
      HistG = new ImageFile(HistPath + "g_" + file);
      HistB = new ImageFile(HistPath + "b_" + file);

      Selection = new ImageFile(SelectionPath + file);

      Directory.CreateDirectory(BasePath);
      Directory.CreateDirectory(ImagePath);
      Directory.CreateDirectory(ThumbsPath);
      Directory.CreateDirectory(FilteredPath);
      Directory.CreateDirectory(HistPath);
      Directory.CreateDirectory(SelectionPath);
    }

    public void Delete()
    {
      Original.Delete();
      Thumb.Delete();
      FilteredGreen.Delete();
      FilteredRed.Delete();
      HistR.Delete();
      HistG.Delete();
      HistB.Delete();
      Selection.Delete();
    }
  }
}
