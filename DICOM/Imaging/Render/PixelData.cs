﻿using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;

using Dicom;
using Dicom.IO.Buffer;

using Dicom.Imaging.Algorithms;
using Dicom.Imaging.LUT;

namespace Dicom.Imaging.Render {
	public interface IPixelData {
		int Width { get; }
		int Height { get; }
		int Components { get; }
		DicomRange<int> GetMinMax(int padding);
		IPixelData Rescale(double scale);
		void Render(ILUT lut, int[] output);
	}

	public static class PixelDataFactory {
		public static IPixelData Create(DicomPixelData pixelData, int frame) {
			PhotometricInterpretation pi = pixelData.PhotometricInterpretation;
			if (pi == PhotometricInterpretation.Monochrome1 || pi == PhotometricInterpretation.Monochrome2 || pi == PhotometricInterpretation.PaletteColor) {
				if (pixelData.BitsAllocated <= 8)
					return new GrayscalePixelDataU8(pixelData.Width, pixelData.Height, pixelData.GetFrame(frame));
				else if (pixelData.BitsAllocated <= 16) {
					if (pixelData.PixelRepresentation == PixelRepresentation.Signed)
						return new GrayscalePixelDataS16(pixelData.Width, pixelData.Height, pixelData.BitDepth, pixelData.GetFrame(frame));
					else
						return new GrayscalePixelDataU16(pixelData.Width, pixelData.Height, pixelData.BitDepth, pixelData.GetFrame(frame));
				} else if (pixelData.BitsAllocated <= 32) {
                    if (pixelData.PixelRepresentation == PixelRepresentation.Signed)
						return new GrayscalePixelDataS32(pixelData.Width, pixelData.Height, pixelData.BitDepth, pixelData.GetFrame(frame));
                    else
						return new GrayscalePixelDataU32(pixelData.Width, pixelData.Height, pixelData.BitDepth, pixelData.GetFrame(frame));
				} else
					throw new DicomImagingException("Unsupported pixel data value for bits stored: {0}", pixelData.BitsStored);
			} else if (pi == PhotometricInterpretation.Rgb || pi == PhotometricInterpretation.YbrFull) {
				var buffer = pixelData.GetFrame(frame);
				if (pixelData.PlanarConfiguration == PlanarConfiguration.Planar)
					buffer = PixelDataConverter.PlanarToInterleaved24(buffer);
				return new ColorPixelData24(pixelData.Width, pixelData.Height, buffer);
			} else {
				throw new DicomImagingException("Unsupported pixel data photometric interpretation: {0}", pi.Value);
			}
		}

		public static SingleBitPixelData Create(DicomOverlayData overlayData) {
			return new SingleBitPixelData(overlayData.Columns, overlayData.Rows, overlayData.Data);
		}
	}

	public class GrayscalePixelDataU8 : IPixelData {
		#region Private Members
		int _width;
		int _height;
		byte[] _data;
		#endregion

		#region Public Constructor
		public GrayscalePixelDataU8(int width, int height, IByteBuffer data) {
			_width = width;
			_height = height;
			_data = data.Data;
		}

		private GrayscalePixelDataU8(int width, int height, byte[] data) {
			_width = width;
			_height = height;
			_data = data;
		}
		#endregion

		#region Public Properties
		public int Width {
			get { return _width; }
		}

		public int Height {
			get { return _height; }
		}

		public int Components {
			get { return 1; }
		}

		public byte[] Data {
			get { return _data; }
		}
		#endregion

		#region Public Methods
		public DicomRange<int> GetMinMax(int padding) {
			int min = Int32.MaxValue;
			int max = Int32.MinValue;

			for (int i = 0; i < _data.Length; i++) {
				if (_data[i] == padding)
					continue;
				else if (_data[i] > max)
					max = _data[i];
				else if (_data[i] < min)
					min = _data[i];
			}

			return new DicomRange<int>(min, max);
		}

		public IPixelData Rescale(double scale) {
			if (scale == 1.0)
				return this;
			int w = (int)(Width * scale);
			int h = (int)(Height * scale);
			byte[] data = BilinearInterpolation.RescaleGrayscale(_data, Width, Height, w, h);
			return new GrayscalePixelDataU8(w, h, data);
		}

		public void Render(ILUT lut, int[] output) {
			if (lut == null) {
				Parallel.For(0, Height, y => {
					for (int i = Width * y, e = i + Width; i < e; i++) {
						output[i] = _data[i];
					}
				});
			} else {
				Parallel.For(0, Height, y => {
					for (int i = Width * y, e = i + Width; i < e; i++) {
						output[i] = lut[_data[i]];
					}
				});
			}
		}
		#endregion
	}

	public class SingleBitPixelData : GrayscalePixelDataU8 {
		#region Public Constructor
		public SingleBitPixelData(int width, int height, IByteBuffer data) : base(width, height, new MemoryByteBuffer(ExpandBits(width, height, data.Data))) {
		}
		#endregion

		#region Static Methods
		private const byte One = 1;
		private const byte Zero = 0;

		private static byte[] ExpandBits(int width, int height, byte[] input) {
			BitArray bits = new BitArray(input);
			byte[] output = new byte[width * height];
			for (int i = 0, l = width * height; i < l; i++) {
				output[i] = bits[i] ? One : Zero;
			}
			return output;
		}
		#endregion
	}

	public class GrayscalePixelDataS16 : IPixelData {
		#region Private Members
		int _width;
		int _height;
		short[] _data;
		#endregion

		#region Public Constructor
		public GrayscalePixelDataS16(int width, int height, BitDepth bitDepth, IByteBuffer data) {
			_width = width;
			_height = height;
			_data = ByteBufferEnumerator<short>.Create(data).ToArray();

			if (bitDepth.BitsStored != 16) {
				int sign = 1 << bitDepth.HighBit;
				int mask = (UInt16.MaxValue >> (bitDepth.BitsAllocated - bitDepth.BitsStored));

				Parallel.For(0, _data.Length, (int i) => {
					short d = _data[i];
					if ((d & sign) != 0)
						_data[i] = (short)-(((-d) & mask) + 1);
					else
						_data[i] = (short)(d & mask);
				});
			}
		}

		private GrayscalePixelDataS16(int width, int height, short[] data) {
			_width = width;
			_height = height;
			_data = data;
		}
		#endregion

		#region Public Properties
		public int Width {
			get { return _width; }
		}

		public int Height {
			get { return _height; }
		}

		public int Components {
			get { return 1; }
		}

		public short[] Data {
			get { return _data; }
		}
		#endregion

		#region Public Methods
		public DicomRange<int> GetMinMax(int padding) {
			int min = Int32.MaxValue;
			int max = Int32.MinValue;

			for (int i = 0; i < _data.Length; i++) {
				if (_data[i] == padding)
					continue;
				else if (_data[i] > max)
					max = _data[i];
				else if (_data[i] < min)
					min = _data[i];
			}

			return new DicomRange<int>(min, max);
		}

		public IPixelData Rescale(double scale) {
			if (scale == 1.0)
				return this;
			int w = (int)(Width * scale);
			int h = (int)(Height * scale);
			short[] data = BilinearInterpolation.RescaleGrayscale(_data, Width, Height, w, h);
			return new GrayscalePixelDataS16(w, h, data);
		}

		public void Render(ILUT lut, int[] output) {
			if (lut == null) {
				Parallel.For(0, Height, y => {
					for (int i = Width * y, e = i + Width; i < e; i++) {
						output[i] = _data[i];
					}
				});
			} else {
				Parallel.For(0, Height, y => {
					for (int i = Width * y, e = i + Width; i < e; i++) {
						output[i] = lut[_data[i]];
					}
				});
			}
		}
		#endregion
	}

	public class GrayscalePixelDataU16 : IPixelData {
		#region Private Members
		int _width;
		int _height;
		ushort[] _data;
		#endregion

		#region Public Constructor
		public GrayscalePixelDataU16(int width, int height, BitDepth bitDepth, IByteBuffer data) {
			_width = width;
			_height = height;
			_data = ByteBufferEnumerator<ushort>.Create(data).ToArray();

			if (bitDepth.BitsStored != 16) {
				int mask = (1 << (bitDepth.HighBit + 1)) - 1;

				Parallel.For(0, _data.Length, (int i) => {
					_data[i] = (ushort)(_data[i] & mask);
				});
			}
		}

		private GrayscalePixelDataU16(int width, int height, ushort[] data) {
			_width = width;
			_height = height;
			_data = data;
		}
		#endregion

		#region Public Properties
		public int Width {
			get { return _width; }
		}

		public int Height {
			get { return _height; }
		}

		public int Components {
			get { return 1; }
		}

		public ushort[] Data {
			get { return _data; }
		}
		#endregion

		#region Public Methods
		public DicomRange<int> GetMinMax(int padding) {
			int min = Int32.MaxValue;
			int max = Int32.MinValue;

			for (int i = 0; i < _data.Length; i++) {
				if (_data[i] == padding)
					continue;
				else if (_data[i] > max)
					max = _data[i];
				else if (_data[i] < min)
					min = _data[i];
			}

			return new DicomRange<int>(min, max);
		}

		public IPixelData Rescale(double scale) {
			if (scale == 1.0)
				return this;
			int w = (int)(Width * scale);
			int h = (int)(Height * scale);
			ushort[] data = BilinearInterpolation.RescaleGrayscale(_data, Width, Height, w, h);
			return new GrayscalePixelDataU16(w, h, data);
		}

		public void Render(ILUT lut, int[] output) {
			if (lut == null) {
				Parallel.For(0, Height, y => {
					for (int i = Width * y, e = i + Width; i < e; i++) {
						output[i] = _data[i];
					}
				});
			} else {
				Parallel.For(0, Height, y => {
					for (int i = Width * y, e = i + Width; i < e; i++) {
						output[i] = lut[_data[i]];
					}
				});
			}
		}
		#endregion
	}


    public class GrayscalePixelDataS32 : IPixelData {
        #region Private Members
        int _width;
        int _height;
        int[] _data;
        #endregion

        #region Public Constructor
        public GrayscalePixelDataS32(int width, int height, BitDepth bitDepth, IByteBuffer data) {
            _width = width;
            _height = height;
            _data = ByteBufferEnumerator<int>.Create(data).ToArray();

            int sign = 1 << bitDepth.HighBit;
			uint mask = (UInt32.MaxValue >> (bitDepth.BitsAllocated - bitDepth.BitsStored));

            Parallel.For(0, _data.Length, (int i) => {
                int d = _data[i];
                if ((d & sign) != 0)
					_data[i] = (int)-(((-d) & mask) + 1);
                else
                    _data[i] = (int)(d & mask);
            });
        }

        private GrayscalePixelDataS32(int width, int height, int[] data) {
            _width = width;
            _height = height;
            _data = data;
        }
        #endregion

        #region Public Properties
        public int Width
        {
            get { return _width; }
        }

        public int Height
        {
            get { return _height; }
        }

        public int Components
        {
            get { return 1; }
        }

        public int[] Data
        {
            get { return _data; }
        }
        #endregion

        #region Public Methods
		public DicomRange<int> GetMinMax(int padding) {
			int min = Int32.MaxValue;
			int max = Int32.MinValue;

			for (int i = 0; i < _data.Length; i++) {
				if (_data[i] == padding)
					continue;
				else if (_data[i] > max)
					max = _data[i];
				else if (_data[i] < min)
					min = _data[i];
			}

			return new DicomRange<int>(min, max);
		}

        public IPixelData Rescale(double scale)
        {
            if (scale == 1.0)
                return this;
            int w = (int)(Width * scale);
            int h = (int)(Height * scale);
            int[] data = BilinearInterpolation.RescaleGrayscale(_data, Width, Height, w, h);
            return new GrayscalePixelDataS32(w, h, data);
        }

        public void Render(ILUT lut, int[] output)
        {
            if (lut == null)
            {
                Parallel.For(0, Height, y =>
                {
                    for (int i = Width * y, e = i + Width; i < e; i++)
                    {
                        output[i] = _data[i];
                    }
                });
            }
            else
            {
                Parallel.For(0, Height, y =>
                {
                    for (int i = Width * y, e = i + Width; i < e; i++)
                    {
                        output[i] = lut[_data[i]];
                    }
                });
            }
        }
        #endregion
    }

    public class GrayscalePixelDataU32 : IPixelData
    {
        #region Private Members
        int _width;
        int _height;
        uint[] _data;
        #endregion

        #region Public Constructor
        public GrayscalePixelDataU32(int width, int height, BitDepth bitDepth, IByteBuffer data)
        {
            _width = width;
            _height = height;
            _data = ByteBufferEnumerator<uint>.Create(data).ToArray();

            if (bitDepth.BitsStored != 32)
            {
                int mask = (1 << (bitDepth.HighBit + 1)) - 1;

                Parallel.For(0, _data.Length, (int i) =>
                {
                    _data[i] = (uint)(_data[i] & mask);
                });
            }
        }

        private GrayscalePixelDataU32(int width, int height, uint[] data)
        {
            _width = width;
            _height = height;
            _data = data;
        }
        #endregion

        #region Public Properties
        public int Width
        {
            get { return _width; }
        }

        public int Height
        {
            get { return _height; }
        }

        public int Components
        {
            get { return 1; }
        }

        public uint[] Data
        {
            get { return _data; }
        }
        #endregion

        #region Public Methods
		public DicomRange<int> GetMinMax(int padding) {
			throw new InvalidOperationException("Calculation of min/max pixel values is not supported for 32-bit unsigned integer data.");
		}

        public IPixelData Rescale(double scale)
        {
            if (scale == 1.0)
                return this;
            int w = (int)(Width * scale);
            int h = (int)(Height * scale);
            uint[] data = BilinearInterpolation.RescaleGrayscale(_data, Width, Height, w, h);
            return new GrayscalePixelDataU32(w, h, data);
        }

        public void Render(ILUT lut, int[] output)
        {
            if (lut == null)
            {
                Parallel.For(0, Height, y =>
                {
                    for (int i = Width * y, e = i + Width; i < e; i++)
                    {
                        output[i] = (int)_data[i];
                    }
                });
            }
            else
            {
                Parallel.For(0, Height, y =>
                {
                    for (int i = Width * y, e = i + Width; i < e; i++)
                    {
                        output[i] = lut[(int)_data[i]];
                    }
                });
            }
        }
        #endregion
    }

    public class ColorPixelData24 : IPixelData
    {
		#region Private Members
		int _width;
		int _height;
		byte[] _data;
		#endregion

		#region Public Constructor
		public ColorPixelData24(int width, int height, IByteBuffer data) {
			_width = width;
			_height = height;
			_data = data.Data;
		}

		private ColorPixelData24(int width, int height, byte[] data) {
			_width = width;
			_height = height;
			_data = data;
		}
		#endregion

		#region Public Properties
		public int Width {
			get { return _width; }
		}

		public int Height {
			get { return _height; }
		}

		public int Components {
			get { return 3; }
		}

		public byte[] Data {
			get { return _data; }
		}
		#endregion

		#region Public Methods
		public DicomRange<int> GetMinMax(int padding) {
			throw new InvalidOperationException("Calculation of min/max pixel values is not supported for 24-bit color pixel data.");
		}

		public IPixelData Rescale(double scale) {
			if (scale == 1.0)
				return this;
			int w = (int)(Width * scale);
			int h = (int)(Height * scale);
			byte[] data = BilinearInterpolation.RescaleColor24(_data, Width, Height, w, h);
			return new ColorPixelData24(w, h, data);
		}

		public void Render(ILUT lut, int[] output) {
			if (lut == null) {
				Parallel.For(0, Height, y => {
					for (int i = Width * y, e = i + Width, p = i * 3; i < e; i++) {
						output[i] = (_data[p++] << 16) | (_data[p++] << 8) | _data[p++];
					}
				});
			} else {
				Parallel.For(0, Height, y => {
					for (int i = Width * y, e = i + Width, p = i * 3; i < e; i++) {
						output[i] = (lut[_data[p++]] << 16) | (lut[_data[p++]] << 8) | lut[_data[p++]];
					}
				});
			}
		}
		#endregion
	}
}
