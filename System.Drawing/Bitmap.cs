//
// System.Drawing.Bitmap.cs
//
// Copyright (C) 2002 Ximian, Inc.  http://www.ximian.com
// Copyright (C) 2004-2005 Novell, Inc (http://www.novell.com)
// Copyright 2011 Xamarin Inc.
//
// Authors: 
//	Alexandre Pigolkine (pigolkine@gmx.de)
//	Christian Meyer (Christian.Meyer@cs.tum.edu)
//	Miguel de Icaza (miguel@ximian.com)
//	Jordi Mas i Hernandez (jmas@softcatala.org)
//	Ravindra (rkumar@novell.com)
//	Sebastien Pouliot  <sebastien@xamarin.com>
//	Kenneth J. Pouncey  <kjpou@pt.lu>
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.IO;
using System.Drawing.Imaging;
using System.Runtime.Serialization;
using System.Runtime.InteropServices;
using System.ComponentModel;

#if MONOMAC
using MonoMac.CoreGraphics;
using MonoMac.Foundation;
using MonoMac.AppKit;
using MonoMac.ImageIO;
#else
using MonoTouch.CoreGraphics;
using MonoTouch.UIKit;
using MonoTouch.Foundation;
using MonoTouch.ImageIO;
using MonoTouch.MobileCoreServices;
#endif

namespace System.Drawing {
	
	[Serializable]
	public sealed class Bitmap : Image {
		// if null, we created the bitmap in memory, otherwise, the backing file.

		internal IntPtr bitmapBlock;

		// we will default this to one for now until we get some tests for other image types
		internal int frameCount = 1;

		internal PixelFormat pixelFormat;
		internal float dpiWidth = 0;
		internal float dpiHeight = 0;
		internal Size imageSize = Size.Empty;
		internal SizeF physicalDimension = SizeF.Empty;
		internal ImageFormat rawFormat;

		private CGDataProvider dataProvider;



		public Bitmap (string filename)
		{
			// Use Image IO
			dataProvider = new CGDataProvider(filename);

			InitializeImageFrame (0);

		}

		public Bitmap (Stream stream, bool useIcm)
		{
			// false: stream is owned by user code
			//nativeObject = InitFromStream (stream);
			// TODO
			// Use Image IO
			byte[] buffer;
			using(var memoryStream = new MemoryStream())
			{
				stream.CopyTo(memoryStream);
				buffer = memoryStream.ToArray();
			}

			dataProvider = new CGDataProvider(buffer, 0, buffer.Length);

			InitializeImageFrame (0);
		}

		public Bitmap (int width, int height) : 
			this (width, height, PixelFormat.Format32bppArgb)
		{
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="System.Drawing.Bitmap"/> class from the specified existing image..
		/// </summary>
		/// <param name="image">Image.</param>
		public Bitmap (Image image) :
			this (image, image.Width, image.Height)
		{

		}

		public Bitmap (Image original, int width, int height) : 
			this (width, height, PixelFormat.Format32bppArgb)
		{
			using (Graphics graphics = Graphics.FromImage (this)) {
				graphics.DrawImage (original, 0, 0, width, height);
			}
		}

		public Bitmap (Image original, Size newSize) : 
			this (newSize.Width, newSize.Height, PixelFormat.Format32bppArgb)
		{
			using (Graphics graphics = Graphics.FromImage (this)) {
				graphics.DrawImage (original, 0, 0, newSize.Width, newSize.Height);
			}
		}

		public Bitmap (int width, int height, PixelFormat format)
		{
			imageTransform = new CGAffineTransform(1, 0, 0, -1, 0, height);

			int bitsPerComponent, bytesPerRow;
			CGColorSpace colorSpace;
			CGBitmapFlags bitmapInfo;
			bool premultiplied = false;
			int bitsPerPixel = 0;

			pixelFormat = format;

			// Don't forget to set the Image width and height for size.
			imageSize.Width = width;
			imageSize.Height = height;

			switch (format){
			case PixelFormat.Format32bppPArgb:
			case PixelFormat.DontCare:
				premultiplied = true;
				colorSpace = CGColorSpace.CreateDeviceRGB ();
				bitsPerComponent = 8;
				bitsPerPixel = 32;
				bitmapInfo = CGBitmapFlags.PremultipliedFirst;
				break;
			case PixelFormat.Format32bppArgb:
				colorSpace = CGColorSpace.CreateDeviceRGB ();
				bitsPerComponent = 8;
				bitsPerPixel = 32;
				bitmapInfo = CGBitmapFlags.PremultipliedFirst;
				break;
			case PixelFormat.Format32bppRgb:
				colorSpace = CGColorSpace.CreateDeviceRGB ();
				bitsPerComponent = 8;
				bitsPerPixel = 32;
				bitmapInfo = CGBitmapFlags.NoneSkipLast;
				break;
			case PixelFormat.Format24bppRgb:
				colorSpace = CGColorSpace.CreateDeviceRGB ();
				bitsPerComponent = 8;
				bitsPerPixel = 32;
				bitmapInfo = CGBitmapFlags.NoneSkipLast;
			default:
				throw new Exception ("Format not supported: " + format);
			}
			bytesPerRow = width * bitsPerPixel/bitsPerComponent;
			int size = bytesPerRow * height;

			bitmapBlock = Marshal.AllocHGlobal (size);
			var bitmap = new CGBitmapContext (bitmapBlock, 
			                              width, height, 
			                              bitsPerComponent, 
			                              bytesPerRow,
			                              colorSpace,
			                              CGImageAlphaInfo.PremultipliedLast);
			// This works for now but we need to look into initializing the memory area itself
			// TODO: Look at what we should do if the image does not have alpha channel
			bitmap.ClearRect (new RectangleF (0,0,width,height));

			var provider = new CGDataProvider (bitmapBlock, size, true);
			NativeCGImage = new CGImage (width, height, bitsPerComponent, bitsPerPixel, bytesPerRow, colorSpace, bitmapInfo, provider, null, false, CGColorRenderingIntent.Default);

		}

		private void InitializeImageFrame(int frame)
		{
			imageTransform = CGAffineTransform.MakeIdentity();

			SetImageInformation (frame);
			var cg = CGImageSource.FromDataProvider(dataProvider).CreateImage(frame, null);
			imageTransform = new CGAffineTransform(1, 0, 0, -1, 0, cg.Height);
			//InitWithCGImage (cg);
			NativeCGImage = cg;

			GuessPixelFormat ();
		}

		private void GuessPixelFormat()
		{
			bool hasAlpha;
			CGColorSpace colorSpace;
			int bitsPerComponent;
			bool premultiplied = false;
			int bitsPerPixel = 0;
			CGImageAlphaInfo alphaInfo;

			var image = NativeCGImage;

			if (image == null) {
				throw new ArgumentException (" image is invalid! " );
			}

			alphaInfo = image.AlphaInfo;
			hasAlpha = ((alphaInfo == CGImageAlphaInfo.PremultipliedLast) || (alphaInfo == CGImageAlphaInfo.PremultipliedFirst) || (alphaInfo == CGImageAlphaInfo.Last) || (alphaInfo == CGImageAlphaInfo.First) ? true : false);

			imageSize.Width = image.Width;
			imageSize.Height = image.Height;

			// Not sure yet if we need to keep the original image information
			// before we change it internally.  TODO look at what windows does
			// and follow that.
			bitsPerComponent = image.BitsPerComponent;
			bitsPerPixel = image.BitsPerPixel;

			colorSpace = image.ColorSpace;

			if (colorSpace != null)
			{
				if (colorSpace.Model == CGColorSpaceModel.RGB) {
					if (bitsPerPixel == 32) {
						if (hasAlpha) {
							if (alphaInfo == CGImageAlphaInfo.PremultipliedFirst) 
							{
								premultiplied = true;
								pixelFormat = PixelFormat.Format32bppPArgb;
							}

							if (alphaInfo == CGImageAlphaInfo.First)
								pixelFormat = PixelFormat.Format32bppArgb;

							if (alphaInfo == CGImageAlphaInfo.Last)
								pixelFormat = PixelFormat.Format32bppRgb;

							if (alphaInfo == CGImageAlphaInfo.PremultipliedLast) 
							{
								premultiplied = true;
								pixelFormat = PixelFormat.Format32bppRgb;
							}


						} else {
							pixelFormat = PixelFormat.Format24bppRgb;
						}
					} else {
						// Right now microsoft looks like it is using Format32bppRGB for other
						// need more test cases to verify
						pixelFormat = PixelFormat.Format32bppArgb;
					}
				} else {
					// Right now microsoft looks like it is using Format32bppRGB for other
					// MonoChrome is set to 32bpppArgb
					// need more test cases to verify
					pixelFormat = PixelFormat.Format32bppArgb;
				}

			}
			else
			{
				// need more test cases to verify
				pixelFormat = PixelFormat.Format32bppArgb;

			}


		}


		private void SetImageInformation(int frame)
		{
			var imageSource = CGImageSource.FromDataProvider (dataProvider);

			frameCount = imageSource.ImageCount;

			var properties = imageSource.GetProperties (frame, null);

			// This needs to be incorporated in frame information later
			// as well as during the clone methods.
			dpiWidth =  properties.DPIWidthF != null ? (float)properties.DPIWidthF : ConversionHelpers.MS_DPI;
			dpiHeight = properties.DPIWidthF != null ? (float)properties.DPIHeightF : ConversionHelpers.MS_DPI;

			physicalDimension.Width = (float)properties.PixelWidth;
			physicalDimension.Height = (float)properties.PixelHeight;


			// The physical size may be off on certain implementations.  For instance the dpiWidth and dpiHeight 
			// are read using integers in core graphics but in windows it is a float.
			// For example:
			// coregraphics dpiWidth = 24 as integer
			// windows dpiWidth = 24.999935 as float
			// this gives a few pixels difference when calculating the physical size.
			// 256 * 96 / 24 = 1024
			// 256 * 96 / 24.999935 = 983.04
			//
			// https://bugzilla.xamarin.com/show_bug.cgi?id=14365
			// PR: https://github.com/mono/maccore/pull/57
			//

			physicalSize = new SizeF (physicalDimension.Width, physicalDimension.Height);
			physicalSize.Width *= ConversionHelpers.MS_DPI / dpiWidth;
			physicalSize.Height *= ConversionHelpers.MS_DPI / dpiHeight;

			// Set the raw image format
			// We will use the UTI from the image source
			switch (imageSource.TypeIdentifier) 
			{
			case "public.png":
				rawFormat = ImageFormat.Png;
				break;
			case "com.microsoft.bmp":
				rawFormat = ImageFormat.Bmp;
				break;
			case "com.compuserve.gif":
				rawFormat = ImageFormat.Gif;
				break;
			case "public.jpeg":
				rawFormat = ImageFormat.Jpeg;
				break;
			case "public.tiff":
				rawFormat = ImageFormat.Tiff;
				break;
			case "com.microsoft.ico":
				rawFormat = ImageFormat.Icon;
				break;
			case "com.adobe.pdf":
				rawFormat = ImageFormat.Wmf;
				break;
			default:
				rawFormat = ImageFormat.Png;
				break;
			}

		}

		private void InitWithCGImage (CGImage image)
		{
			int	width, height;
			CGBitmapContext bitmap = null;
			bool hasAlpha;
			CGImageAlphaInfo alphaInfo;
			CGColorSpace colorSpace;
			int bitsPerComponent, bytesPerRow;
			CGBitmapFlags bitmapInfo;
			bool premultiplied = false;
			int bitsPerPixel = 0;

			if (image == null) {
				throw new ArgumentException (" image is invalid! " );
			}

			alphaInfo = image.AlphaInfo;
			hasAlpha = ((alphaInfo == CGImageAlphaInfo.PremultipliedLast) || (alphaInfo == CGImageAlphaInfo.PremultipliedFirst) || (alphaInfo == CGImageAlphaInfo.Last) || (alphaInfo == CGImageAlphaInfo.First) ? true : false);
			
			imageSize.Width = image.Width;
			imageSize.Height = image.Height;
			
			width = image.Width;
			height = image.Height;

			// Not sure yet if we need to keep the original image information
			// before we change it internally.  TODO look at what windows does
			// and follow that.
			bitmapInfo = image.BitmapInfo;
			bitsPerComponent = image.BitsPerComponent;
			bitsPerPixel = image.BitsPerPixel;
			bytesPerRow = width * bitsPerPixel/bitsPerComponent;
			int size = bytesPerRow * height;
			
			colorSpace = image.ColorSpace;

			// Right now internally we represent the images all the same
			// I left the call here just in case we find that this is not
			// possible.  Read the comments for non alpha images.
			if(colorSpace != null) {
				if( hasAlpha ) {
					premultiplied = true;
					colorSpace = CGColorSpace.CreateDeviceRGB ();
					bitsPerComponent = 8;
					bitsPerPixel = 32;
					bitmapInfo = CGBitmapFlags.PremultipliedLast;
				}
				else
				{
					// even for images without alpha we will internally 
					// represent them as RGB with alpha.  There were problems
					// if we do not do it this way and creating a bitmap context.
					// The images were not drawing correctly and tearing.  Also
					// creating a Graphics to draw on was a nightmare.  This
					// should probably be looked into or maybe it is ok and we
					// can continue representing internally with this representation
					premultiplied = true;
					colorSpace = CGColorSpace.CreateDeviceRGB ();
					bitsPerComponent = 8;
					bitsPerPixel = 32;
					bitmapInfo = CGBitmapFlags.NoneSkipLast;
				}
			} else {
				premultiplied = true;
				colorSpace = CGColorSpace.CreateDeviceRGB ();
				bitsPerComponent = 8;
				bitsPerPixel = 32;
				bitmapInfo = CGBitmapFlags.NoneSkipLast;
			}

			bytesPerRow = width * bitsPerPixel/bitsPerComponent;
			size = bytesPerRow * height;

			bitmapBlock = Marshal.AllocHGlobal (size);
			bitmap = new CGBitmapContext (bitmapBlock, 
			                              width, height, 
			                              bitsPerComponent, 
			                              bytesPerRow,
			                              colorSpace,
			                              bitmapInfo);

			bitmap.ClearRect (new RectangleF (0,0,width,height));

			// We need to flip the Y axis to go from right handed to lefted handed coordinate system
			var transform = new CGAffineTransform(1, 0, 0, -1, 0, image.Height);
			bitmap.ConcatCTM(transform);

			bitmap.DrawImage(new RectangleF (0, 0, image.Width, image.Height), image);

			var provider = new CGDataProvider (bitmapBlock, size, true);
			NativeCGImage = new CGImage (width, height, bitsPerComponent, 
			                             bitsPerPixel, bytesPerRow, 
			                             colorSpace,
			                             bitmapInfo,
			                             provider, null, true, image.RenderingIntent);

			colorSpace.Dispose();
			bitmap.Dispose();
		}

		internal CGBitmapContext GetRenderableContext()
		{

			var format = GetBestSupportedFormat (pixelFormat);
			var bitmapContext = CreateCompatibleBitmapContext (NativeCGImage.Width, NativeCGImage.Height, format);

			bitmapContext.DrawImage (new RectangleF (0, 0, NativeCGImage.Width, NativeCGImage.Height), NativeCGImage);

			int size = bitmapContext.BytesPerRow * bitmapContext.Height;
			var provider = new CGDataProvider (bitmapContext.Data, size, true);

			NativeCGImage = new CGImage (bitmapContext.Width, bitmapContext.Height, bitmapContext.BitsPerComponent, 
			                             bitmapContext.BitsPerPixel, bitmapContext.BytesPerRow, 
			                             bitmapContext.ColorSpace,
			                             bitmapContext.AlphaInfo,
			                             provider, null, true, CGColorRenderingIntent.Default);
			return bitmapContext;
		}

		internal void RotateFlip (RotateFlipType rotateFlipType)
		{

			CGAffineTransform rotateFlip;

			int width, height;
			width = NativeCGImage.Width;
			height = NativeCGImage.Height;

			switch (rotateFlipType) 
			{
				//			case RotateFlipType.RotateNoneFlipNone:
				//			//case RotateFlipType.Rotate180FlipXY:
				//				rotateFlip = GeomUtilities.CreateRotateFlipTransform (b.Width, b.Height, 0, false, false);
				//				break;
				case RotateFlipType.Rotate90FlipNone:
				//case RotateFlipType.Rotate270FlipXY:
				rotateFlip = GeomUtilities.CreateRotateFlipTransform (ref width, ref height, 90, false, false);
				break;
				case RotateFlipType.Rotate180FlipNone:
				//case RotateFlipType.RotateNoneFlipXY:
				rotateFlip = GeomUtilities.CreateRotateFlipTransform (ref width, ref height, 0, true, true);
				break;
				case RotateFlipType.Rotate270FlipNone:
				//case RotateFlipType.Rotate90FlipXY:
				rotateFlip = GeomUtilities.CreateRotateFlipTransform (ref width, ref height, 270, false, false);
				break;
				case RotateFlipType.RotateNoneFlipX:
				//case RotateFlipType.Rotate180FlipY:
				rotateFlip = GeomUtilities.CreateRotateFlipTransform (ref width, ref height, 0, true, false);
				break;
				case RotateFlipType.Rotate90FlipX:
				//case RotateFlipType.Rotate270FlipY:
				rotateFlip = GeomUtilities.CreateRotateFlipTransform (ref width, ref height, 90, true, false);
				break;
				case RotateFlipType.Rotate180FlipX:
				//case RotateFlipType.RotateNoneFlipY:
				rotateFlip = GeomUtilities.CreateRotateFlipTransform (ref width, ref height, 0, false, true);
				break;
				case RotateFlipType.Rotate270FlipX:
				//case RotateFlipType.Rotate90FlipY:
				rotateFlip = GeomUtilities.CreateRotateFlipTransform (ref width, ref height, 270, true, false);
				break;
			}

			var format = GetBestSupportedFormat (pixelFormat);
			var bitmapContext = CreateCompatibleBitmapContext (width, height, format);

			bitmapContext.ConcatCTM (rotateFlip);

			bitmapContext.DrawImage (new RectangleF (0, 0, NativeCGImage.Width, NativeCGImage.Height), NativeCGImage);

			int size = bitmapContext.BytesPerRow * bitmapContext.Height;
			var provider = new CGDataProvider (bitmapContext.Data, size, true);

			// If the width or height is not the seme we need to switch the dpiHeight and dpiWidth
			// We should be able to get around this with set resolution later.
			if (NativeCGImage.Width != width || NativeCGImage.Height != height)
			{
				var temp = dpiWidth;
				dpiHeight = dpiWidth;
				dpiWidth = temp;
			}

			NativeCGImage = new CGImage (bitmapContext.Width, bitmapContext.Height, bitmapContext.BitsPerComponent, 
			                             bitmapContext.BitsPerPixel, bitmapContext.BytesPerRow, 
			                             bitmapContext.ColorSpace,
			                             bitmapContext.AlphaInfo,
			                             provider, null, true, CGColorRenderingIntent.Default);


			physicalDimension.Width = (float)width;
			physicalDimension.Height = (float)height;

			physicalSize = new SizeF (physicalDimension.Width, physicalDimension.Height);
			physicalSize.Width *= ConversionHelpers.MS_DPI / dpiWidth;
			physicalSize.Height *= ConversionHelpers.MS_DPI / dpiHeight;

			// In windows the RawFormat is changed to MemoryBmp to show that the image has changed.
			rawFormat = ImageFormat.MemoryBmp;

			// Set our transform for this image for the new height
			imageTransform = new CGAffineTransform(1, 0, 0, -1, 0, height);

		}

		private PixelFormat GetBestSupportedFormat (PixelFormat pixelFormat)
		{
			switch (pixelFormat) 
			{
			case PixelFormat.Format32bppArgb:
				return PixelFormat.Format32bppArgb;
			case PixelFormat.Format32bppPArgb:
				return PixelFormat.Format32bppPArgb;
			case PixelFormat.Format32bppRgb:
				return PixelFormat.Format32bppRgb;
			case PixelFormat.Format24bppRgb:
				return PixelFormat.Format24bppRgb;
			default:
				return PixelFormat.Format32bppArgb;
			}

		}

		private CGBitmapContext CreateCompatibleBitmapContext(int width, int height, PixelFormat pixelFormat)
		{
			int bitsPerComponent, bytesPerRow;
			CGColorSpace colorSpace;
			CGImageAlphaInfo alphaInfo;
			bool premultiplied = false;
			int bitsPerPixel = 0;

			// CoreGraphics only supports a few options so we have to make do with what we have
			// https://developer.apple.com/library/mac/qa/qa1037/_index.html
			switch (pixelFormat)
			{
			case PixelFormat.Format32bppPArgb:
			case PixelFormat.DontCare:
				premultiplied = true;
				colorSpace = CGColorSpace.CreateDeviceRGB ();
				bitsPerComponent = 8;
				bitsPerPixel = 32;
				alphaInfo = CGImageAlphaInfo.PremultipliedLast;
				break;
			case PixelFormat.Format32bppArgb:
				colorSpace = CGColorSpace.CreateDeviceRGB ();
				bitsPerComponent = 8;
				bitsPerPixel = 32;
				alphaInfo = CGImageAlphaInfo.PremultipliedLast;
				break;
			case PixelFormat.Format32bppRgb:
				colorSpace = CGColorSpace.CreateDeviceRGB ();
				bitsPerComponent = 8;
				bitsPerPixel = 32;
				alphaInfo = CGImageAlphaInfo.PremultipliedLast;
				break;
			case PixelFormat.Format24bppRgb:
				colorSpace = CGColorSpace.CreateDeviceRGB ();
				bitsPerComponent = 8;
				bitsPerPixel = 32;
				alphaInfo = CGImageAlphaInfo.PremultipliedLast;
				break;
			default:
				throw new Exception ("Format not supported: " + pixelFormat);
			}

			bytesPerRow = width * bitsPerPixel/bitsPerComponent;
			int size = bytesPerRow * height;

			var bitmapBlock = Marshal.AllocHGlobal (size);
			var bitmap = new CGBitmapContext (bitmapBlock, 
			                                  width, height, 
			                                  bitsPerComponent, 
			                                  bytesPerRow,
			                                  colorSpace,
			                                  alphaInfo);

			bitmap.ClearRect (new RectangleF (0,0,width,height));

			colorSpace.Dispose ();

			return bitmap;

		}

		/*
		  * perform an in-place swap from Quadrant 1 to Quadrant III format
		  * (upside-down PostScript/GL to right side up QD/CG raster format)
		  * We do this in-place, which requires more copying, but will touch
		  * only half the pages.  (Display grabs are BIG!)
		  *
		  * Pixel reformatting may optionally be done here if needed.
		*/
		private void flipImageYAxis (IntPtr source, IntPtr dest, int stride, int height, int size)
		{
			
			long top, bottom;
			byte[] buffer;
			long topP;
			long bottomP;
			long rowBytes;
			
			top = 0;
			bottom = height - 1;
			rowBytes = stride;
			
			var mData = new byte[size];
			Marshal.Copy(source, mData, 0, size);
			
			buffer = new byte[rowBytes];
			
			while (top < bottom) {
				topP = top * rowBytes;
				bottomP = bottom * rowBytes;
				
				/*
				 * Save and swap scanlines.
				 *
				 * This code does a simple in-place exchange with a temp buffer.
				 * If you need to reformat the pixels, replace the first two Array.Copy
				 * calls with your own custom pixel reformatter.
				 */
				Array.Copy (mData, topP, buffer, 0, rowBytes);
				Array.Copy (mData, bottomP, mData, topP, rowBytes);
				Array.Copy (buffer, 0, mData, bottomP, rowBytes);
				
				++top;
				--bottom;
				
			}
			
			Marshal.Copy(mData, 0, dest, size);
			
		}


		/*
		  * perform an in-place swap from Quadrant 1 to Quadrant III format
		  * (upside-down PostScript/GL to right side up QD/CG raster format)
		  * We do this in-place, which requires more copying, but will touch
		  * only half the pages.  (Display grabs are BIG!)
		  *
		  * Pixel reformatting may optionally be done here if needed.
		  * 
		  * NOTE: Not used right now
		*/
		private void flipImageYAxis (int width, int height, int size)
		{
			
			long top, bottom;
			byte[] buffer;
			long topP;
			long bottomP;
			long rowBytes;
			
			top = 0;
			bottom = height - 1;
			rowBytes = width;

			var mData = new byte[size];
			Marshal.Copy(bitmapBlock, mData, 0, size);

			buffer = new byte[rowBytes];
			
			while (top < bottom) {
				topP = top * rowBytes;
				bottomP = bottom * rowBytes;
				
				/*
				 * Save and swap scanlines.
				 *
				 * This code does a simple in-place exchange with a temp buffer.
				 * If you need to reformat the pixels, replace the first two Array.Copy
				 * calls with your own custom pixel reformatter.
				 */
				Array.Copy (mData, topP, buffer, 0, rowBytes);
				Array.Copy (mData, bottomP, mData, topP, rowBytes);
				Array.Copy (buffer, 0, mData, bottomP, rowBytes);
				
				++top;
				--bottom;
				
			}

			Marshal.Copy(mData, 0, bitmapBlock, size);

		}

		/// <summary>
		/// Creates a copy of the section of this Bitmap defined by Rectangle structure and with a specified PixelFormat enumeration.
		/// </summary>
		/// <param name="rect">Rect.</param>
		/// <param name="pixelFormat">Pixel format.</param>
		public Bitmap Clone (Rectangle rect, PixelFormat pixelFormat)
		{
			if (rect.Width == 0 || rect.Height == 0)
				throw new ArgumentException ("Width or Height of rect is 0.");

			var width = rect.Width;
			var height = rect.Height;

			var tmpImg = new Bitmap (width, height, pixelFormat);

			using (Graphics g = Graphics.FromImage (tmpImg)) {
				g.DrawImage (this, new Rectangle(0,0, width, height), rect, GraphicsUnit.Pixel );
			}
			return tmpImg;
		}


		protected override void Dispose (bool disposing)
		{
			if (disposing){
				if (NativeCGImage != null){
					NativeCGImage.Dispose ();
					NativeCGImage = null;
				}
				//Marshal.FreeHGlobal (bitmapBlock);
				bitmapBlock = IntPtr.Zero;
				Console.WriteLine("Bitmap Dispose");
			}
			base.Dispose (disposing);
		}
		
		public Color GetPixel (int x, int y)
		{
			// TODO
			return Color.White;
		}
		
		public void SetResolution (float xDpi, float yDpi)
		{
			throw new NotImplementedException ();
		}
		
		public void Save (string path, ImageFormat format)
		{
			if (path == null)
				throw new ArgumentNullException ("path");
			
			if (NativeCGImage == null)
				throw new ObjectDisposedException ("cgimage");

			// With MonoTouch we can use UTType from CoreMobileServices but since
			// MonoMac does not have that yet (or at least can not find it) I will 
			// use the string version of those for now.  I did not want to add another
			// #if #else in here.


			// for now we will just default this to png
			var typeIdentifier = "public.png";

			// Get the correct type identifier
			if (format == ImageFormat.Bmp)
				typeIdentifier = "com.microsoft.bmp";
//			else if (format == ImageFormat.Emf)
//				typeIdentifier = "image/emf";
//			else if (format == ImageFormat.Exif)
//				typeIdentifier = "image/exif";
			else if (format == ImageFormat.Gif)
				typeIdentifier = "com.compuserve.gif";
			else if (format == ImageFormat.Icon)
				typeIdentifier = "com.microsoft.ico";
			else if (format == ImageFormat.Jpeg)
				typeIdentifier = "public.jpeg";
			else if (format == ImageFormat.Png)
				typeIdentifier = "public.png";
			else if (format == ImageFormat.Tiff)
				typeIdentifier = "public.tiff";
			else if (format == ImageFormat.Wmf)
				typeIdentifier = "com.adobe.pdf";

			// Not sure what this is yet
			else if (format == ImageFormat.MemoryBmp)
				throw new NotImplementedException("ImageFormat.MemoryBmp not supported");

			// Obtain a URL file path to be passed
			NSUrl url = NSUrl.FromFilename(path);

			// * NOTE * we only support one image for right now.

			// Create an image destination that saves into the path that is passed in
			CGImageDestination dest = CGImageDestination.FromUrl (url, typeIdentifier, frameCount, null); 

			// Add an image to the destination
			dest.AddImage(NativeCGImage, null);

			// Finish the export
			bool success = dest.Close ();
//                        if (success == false)
//                                Console.WriteLine("did not work");
//                        else
//                                Console.WriteLine("did work: " + path);
			dest.Dispose();
			dest = null;

		}



		public void Save (string path)
		{
			if (path == null)
				throw new ArgumentNullException ("path");
			var format = ImageFormat.Png;
			
			var p = path.LastIndexOf (".");
			if (p != -1 && p < path.Length){
				switch (path.Substring (p + 1)){
				case "png": break;
				case "jpg": format = ImageFormat.Jpeg; break;
				case "tiff": format = ImageFormat.Tiff; break;
				case "bmp": format = ImageFormat.Bmp; break;
				}
			}
			Save (path, format);
		}
		
		public BitmapData LockBits (RectangleF rect, ImageLockMode flags, PixelFormat format)
		{
			throw new NotImplementedException ();			
		}
		
		public void UnlockBits (BitmapData data)
		{
			throw new NotImplementedException ();
		}
		
	}
}
