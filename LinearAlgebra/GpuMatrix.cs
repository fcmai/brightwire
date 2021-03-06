﻿using ManagedCuda;
using ManagedCuda.CudaBlas;
using MathNet.Numerics.LinearAlgebra.Single;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BrightWire.Models;
using ManagedCuda.BasicTypes;
using BrightWire.Models.Output;

namespace BrightWire.LinearAlgebra
{
    internal class GpuMatrix : IMatrix
    {
        readonly CudaProvider _cuda;
        readonly CudaDeviceVariable<float> _data;
        readonly int _rows, _columns;
        bool _disposed = false, _shouldDispose = true;
#if DEBUG
        static int _gid = 0;
        int _id = _gid++;
        public static int _badAlloc = -1;
        public static int _badDispose = -1;

        public bool IsValid { get { return !_disposed; } }
#else
        public bool IsValid { get { return true; } }
#endif

        public GpuMatrix(CudaProvider cuda, int rows, int columns, Func<int, int, float> init)
        {
            _cuda = cuda;
            _rows = rows;
            _columns = columns;
            cuda.Register(this);

            var count = rows * columns;
            var data = new float[count];
            for (var j = 0; j < columns; j++) {
                for (var i = 0; i < rows; i++) {
                    data[j * rows + i] = init(i, j);
                }
            }
            _data = new CudaDeviceVariable<float>(count);
            _data.CopyToDevice(data);

#if DEBUG
            if (_id == _badAlloc)
                Debugger.Break();
#endif
        }

        public GpuMatrix(CudaProvider cuda, IIndexableMatrix matrix) : this(cuda, matrix.RowCount, matrix.ColumnCount, (j, k) => matrix[j, k])
        {
        }

        internal GpuMatrix(CudaProvider cuda, int rows, int columns, CudaDeviceVariable<float> gpuData, bool shouldRegister = true)
        {
            _cuda = cuda;
            _rows = rows;
            _columns = columns;
            _data = gpuData;
            if(shouldRegister)
                cuda.Register(this);
#if DEBUG
            if (_id == _badAlloc)
                Debugger.Break();
#endif
        }

#if DEBUG
        ~GpuMatrix()
        {
            if(_shouldDispose && !_disposed)
                Debug.WriteLine("\tMatrix {0} was not disposed !!", _id);
        }
#endif

        protected virtual void Dispose(bool disposing)
        {
#if DEBUG
            if (_id == _badDispose)
                Debugger.Break();
#endif
            if (_shouldDispose && disposing && !_disposed) {
                _data.Dispose();
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public override string ToString()
        {
            return AsIndexable().ToString();
        }

        public int ColumnCount
        {
            get
            {
                return _columns;
            }
        }

        public int RowCount
        {
            get
            {
                return _rows;
            }
        }

        public object WrappedObject
        {
            get
            {
                return _data;
            }
        }

        internal CudaDeviceVariable<float> CudaDeviceVariable { get { return _data; } }

        public IMatrix Add(IMatrix matrix)
        {
            Debug.Assert(IsValid && matrix.IsValid);
            var other = (GpuMatrix)matrix;
            Debug.Assert(other._rows == _rows && other._columns == _columns);

            var ret = new CudaDeviceVariable<float>(other._data.Size);
            ret.CopyToDevice(other._data);
            _cuda.Blas.Axpy(1.0f, _data, 1, ret, 1);
            return new GpuMatrix(_cuda, _rows, _columns, ret);
        }

        public void AddInPlace(IMatrix matrix, float coefficient1 = 1, float coefficient2 = 1)
        {
            Debug.Assert(IsValid && matrix.IsValid);
            var other = (GpuMatrix)matrix;
            Debug.Assert(other._rows == _rows && other._columns == _columns);

            _cuda.AddInPlace(_data, other._data, _rows * _columns, coefficient1, coefficient2);
        }

        public void AddToEachColumn(IVector vector)
        {
            Debug.Assert(IsValid && vector.IsValid);
            var other = (GpuVector)vector;
            _cuda.AddToEachColumn(_data, other.CudaDeviceVariable, _rows, _columns);
        }

        public void AddToEachRow(IVector vector)
        {
            Debug.Assert(IsValid && vector.IsValid);
            var other = (GpuVector)vector;
            _cuda.AddToEachRow(_data, other.CudaDeviceVariable, _rows, _columns);
        }

        public IIndexableMatrix AsIndexable()
        {
            Debug.Assert(IsValid);
            var data = new float[_rows * _columns];
            _data.CopyToHost(data);
            return _cuda.NumericsProvider.Create(_rows, _columns, (j, k) => data[k * _rows + j]).AsIndexable();
        }

        public void Clear()
        {
            Debug.Assert(IsValid);
            _data.Memset(0);
        }

        public void ClearColumns(IReadOnlyList<int> indices)
        {
            Debug.Assert(IsValid);
            int offset = 0;
            foreach (var item in indices) {
                _cuda.MemClear(_data, _rows, item * _rows);
                offset += _rows;
            }
        }

        public void ClearRows(IReadOnlyList<int> indices)
        {
            Debug.Assert(IsValid);
            int offset = 0;
            foreach (var item in indices) {
                _cuda.MemClear(_data, _columns, item, _rows);
                offset += 1;
            }
        }

        public IMatrix Clone()
        {
            Debug.Assert(IsValid);
            var ret = new CudaDeviceVariable<float>(_rows * _columns);
            ret.CopyToDevice(_data);
            return new GpuMatrix(_cuda, _rows, _columns, ret);
        }

        public IVector Column(int index)
        {
            Debug.Assert(IsValid);
            var ret = new CudaDeviceVariable<float>(_rows);
            ret.CopyToDevice(_data, index * _rows * sizeof(float), 0, _rows * sizeof(float));
            return new GpuVector(_cuda, _rows, ret);
        }

        public IVector ColumnL2Norm()
        {
            Debug.Assert(IsValid);
            var norm = new List<float>();
            for(var i = 0; i < _columns; i++) {
                using (var col = Column(i))
                    norm.Add(col.L2Norm());
            }
            return _cuda.Create(norm.Count, x => norm[x]);
        }

        public IVector ColumnSums(float coefficient = 1)
        {
            Debug.Assert(IsValid);
            return new GpuVector(_cuda, _columns, _cuda.SumColumns(_data, _rows, _columns));
        }

        public IMatrix ConcatColumns(IMatrix bottom)
        {
            Debug.Assert(IsValid && bottom.IsValid);
            var t = this;
            var b = (GpuMatrix)bottom;
            Debug.Assert(ColumnCount == bottom.ColumnCount);
            var size = t.RowCount + b.RowCount;
            var ret = new CudaDeviceVariable<float>(size * t.ColumnCount);
            _cuda.ConcatColumns(t._data, b._data, ret, size, t.ColumnCount, t.RowCount, b.RowCount);
            return new GpuMatrix(_cuda, size, t.ColumnCount, ret);
        }

        public IMatrix ConcatRows(IMatrix right)
        {
            Debug.Assert(IsValid && right.IsValid);
            var t = this;
            var b = (GpuMatrix)right;
            Debug.Assert(RowCount == right.RowCount);
            var size = t.ColumnCount + b.ColumnCount;
            var ret = new CudaDeviceVariable<float>(t.RowCount * size);
            _cuda.ConcatRows(t._data, b._data, ret, t.RowCount, size, t.ColumnCount);
            return new GpuMatrix(_cuda, t.RowCount, size, ret);
        }

        public void Constrain(float min, float max)
        {
            Debug.Assert(IsValid);
            _cuda.Constrain(_data, _rows * _columns, min, max);
        }

        public IVector Diagonal()
        {
            Debug.Assert(IsValid);
            var ret = _cuda.Diagonal(_data, _rows, _columns);
            return new GpuVector(_cuda, Math.Min(_rows, _columns), ret);
        }

        public IVector GetColumnSegment(int columnIndex, int rowIndex, int length)
        {
            Debug.Assert(IsValid);

            var ret = new CudaDeviceVariable<float>(length);
            ret.CopyToDevice(_data, ((columnIndex * _rows) + rowIndex) * sizeof(float), 0, length * sizeof(float));

            return new GpuVector(_cuda, length, ret);
        }

        public IMatrix GetNewMatrixFromColumns(IReadOnlyList<int> columnIndices)
        {
            Debug.Assert(IsValid);
            int offset = 0;
            var ret = new CudaDeviceVariable<float>(_rows * columnIndices.Count);
            foreach (var item in columnIndices) {
                ret.CopyToDevice(_data, item * _rows * sizeof(float), offset * sizeof(float), _rows * sizeof(float));
                offset += _rows;
            }
            return new GpuMatrix(_cuda, _rows, columnIndices.Count, ret);
        }

        public IMatrix GetNewMatrixFromRows(IReadOnlyList<int> rowIndices)
        {
            Debug.Assert(IsValid);
            int offset = 0;
            var ret = new CudaDeviceVariable<float>(_columns * rowIndices.Count);
            foreach (var item in rowIndices) {
                CudaBlasNativeMethods.cublasScopy_v2(_cuda.Blas.CublasHandle,
                    n: _columns,
                    x: _data.DevicePointer + (item * sizeof(float)),
                    incx: _rows,
                    y: ret.DevicePointer + (offset * sizeof(float)),
                    incy: rowIndices.Count
                );
                offset += 1;
            }
            return new GpuMatrix(_cuda, rowIndices.Count, _columns, ret);
        }

        public IVector GetRowSegment(int rowIndex, int columnIndex, int length)
        {
            Debug.Assert(IsValid);
            int offset = (rowIndex + (columnIndex * _rows)) * sizeof(float);
            var ret = new CudaDeviceVariable<float>(length);
            CudaBlasNativeMethods.cublasScopy_v2(_cuda.Blas.CublasHandle, length, _data.DevicePointer + offset, _rows, ret.DevicePointer, 1);
            return new GpuVector(_cuda, length, ret);
        }

        public void L1Regularisation(float coefficient)
        {
            Debug.Assert(IsValid);
            _cuda.L1Regularisation(_data, _rows * _columns, coefficient);
        }

        public IMatrix LeakyReluActivation()
        {
            Debug.Assert(IsValid);
            var ret = _cuda.LeakyRELU(_data, _rows, _columns);
            return new GpuMatrix(_cuda, _rows, _columns, ret);
        }

        public IMatrix LeakyReluDerivative()
        {
            Debug.Assert(IsValid);
            var ret = _cuda.LeakyRELUDerivative(_data, _rows, _columns);
            return new GpuMatrix(_cuda, _rows, _columns, ret);
        }

        public void Multiply(float scalar)
        {
            Debug.Assert(IsValid);
            _cuda.Blas.Scale(scalar, _data, 1);
        }

        public IMatrix Multiply(IMatrix matrix)
        {
            Debug.Assert(IsValid && matrix.IsValid);
            var other = (GpuMatrix)matrix;
            Debug.Assert(_columns == other._rows);
            var ret = new CudaDeviceVariable<float>(_rows * other.ColumnCount);
            int rowsA = _rows, columnsArowsB = _columns, columnsB = other.ColumnCount;

            float alpha = 1.0f, beta = 0.0f;
            CudaBlasNativeMethods.cublasSgemm_v2(_cuda.Blas.CublasHandle,
                Operation.NonTranspose,
                Operation.NonTranspose,
                rowsA,
                columnsB,
                columnsArowsB,
                ref alpha,
                _data.DevicePointer,
                rowsA,
                other._data.DevicePointer,
                columnsArowsB,
                ref beta,
                ret.DevicePointer,
                rowsA
            );
            return new GpuMatrix(_cuda, _rows, other.ColumnCount, ret);
        }

        public IMatrix PointwiseDivide(IMatrix matrix)
        {
            Debug.Assert(IsValid && matrix.IsValid);
            var other = (GpuMatrix)matrix;
            Debug.Assert(other._rows == _rows && other._columns == _columns);

            var size = _rows * _columns;
            var ret = _cuda.PointwiseDivide(_data, other._data, size);
            return new GpuMatrix(_cuda, _rows, _columns, ret);
        }

        public void PointwiseDivideColumns(IVector vector)
        {
            Debug.Assert(IsValid && vector.IsValid);
            var other = (GpuVector)vector;
            _cuda.PointwiseDivideColumns(_data, other.CudaDeviceVariable, _rows, _columns);
        }

        public void PointwiseDivideRows(IVector vector)
        {
            Debug.Assert(IsValid && vector.IsValid);
            var other = (GpuVector)vector;
            _cuda.PointwiseDivideRows(_data, other.CudaDeviceVariable, _rows, _columns);
        }

        public IMatrix PointwiseMultiply(IMatrix matrix)
        {
            Debug.Assert(IsValid && matrix.IsValid);
            var other = (GpuMatrix)matrix;
            Debug.Assert(other._rows == _rows && other._columns == _columns);

            var size = _rows * _columns;
            var ret = _cuda.PointwiseMultiply(_data, other._data, size);
            return new GpuMatrix(_cuda, _rows, _columns, ret);
        }

        public IMatrix Pow(float power)
        {
            Debug.Assert(IsValid);
            var ret = _cuda.Pow(_data, _rows * _columns, power);
            return new GpuMatrix(_cuda, _rows, _columns, ret);
        }

        public IMatrix ReluActivation()
        {
            Debug.Assert(IsValid);
            return new GpuMatrix(_cuda, _rows, _columns, _cuda.RELU(_data, _rows, _columns));
        }

        public IMatrix ReluDerivative()
        {
            Debug.Assert(IsValid);
            return new GpuMatrix(_cuda, _rows, _columns, _cuda.RELUDerivative(_data, _rows, _columns));
        }

        public IVector Row(int index)
        {
            Debug.Assert(IsValid);
            var ret = new CudaDeviceVariable<float>(_columns);
            int offset = index * sizeof(float);
            CudaBlasNativeMethods.cublasScopy_v2(_cuda.Blas.CublasHandle, _columns, _data.DevicePointer + offset, _rows, ret.DevicePointer, 1);
            return new GpuVector(_cuda, _columns, ret);
        }

        public IVector RowL2Norm()
        {
            Debug.Assert(IsValid);
            var norm = new List<float>();
            for (var i = 0; i < _rows; i++) {
                using (var row = Row(i))
                    norm.Add(row.L2Norm());
            }
            return _cuda.Create(norm.Count, x => norm[x]);
        }

        public IVector RowSums(float coefficient = 1)
        {
            Debug.Assert(IsValid);
            return new GpuVector(_cuda, _rows, _cuda.SumRows(_data, _rows, _columns));
        }

        public IMatrix SigmoidActivation()
        {
            Debug.Assert(IsValid);
            return new GpuMatrix(_cuda, _rows, _columns, _cuda.Sigmoid(_data, _rows, _columns));
        }

        public IMatrix SigmoidDerivative()
        {
            Debug.Assert(IsValid);
            return new GpuMatrix(_cuda, _rows, _columns, _cuda.SigmoidDerivative(_data, _rows, _columns));
        }

        public IMatrix SoftmaxActivation()
        {
            Debug.Assert(IsValid);
            var rowOutput = new List<GpuVector>();
            for (var i = 0; i < _rows; i++) {
                using (var row = Row(i))
                    rowOutput.Add(row.Softmax() as GpuVector);
            }
            var ret = new CudaDeviceVariable<float>(_rows * _columns);
            for (var i = 0; i < _rows; i++) {
                using (var row = rowOutput[i]) {
                    ret.CopyToDevice(row.CudaDeviceVariable, 0, _columns * i * sizeof(float), _columns * sizeof(float));
                    //CudaBlasNativeMethods.cublasScopy_v2(_cuda.Blas.CublasHandle, _columns * sizeof(float), row.CudaDeviceVariable.DevicePointer, sizeof(float), ret.DevicePointer, _rows * sizeof(float));
                }
            }
            //return new GpuMatrix(_cuda, _rows, _columns, ret);
            using(var temp = new GpuMatrix(_cuda, _columns, _rows, ret))
                return temp.Transpose();
        }

        public ColumnSplit SplitColumns(int position)
        {
            Debug.Assert(IsValid);
            int size = _rows - position;
            var ret1 = new CudaDeviceVariable<float>(position * _columns);
            var ret2 = new CudaDeviceVariable<float>(size * _columns);
            _cuda.SplitColumns(_data, ret1, ret2, _rows, _columns, position);
            return new ColumnSplit(new GpuMatrix(_cuda, position, _columns, ret1), new GpuMatrix(_cuda, size, _columns, ret2));
        }

        public RowSplit SplitRows(int position)
        {
            Debug.Assert(IsValid);
            int size = _columns - position;
            var ret1 = new CudaDeviceVariable<float>(_rows * position);
            var ret2 = new CudaDeviceVariable<float>(_rows * size);
            _cuda.SplitRows(_data, ret1, ret2, _rows, _columns, position);
            return new RowSplit(new GpuMatrix(_cuda, _rows, position, ret1), new GpuMatrix(_cuda, _rows, size, ret2));
        }

        public IMatrix Sqrt(float valueAdjustment = 0)
        {
            Debug.Assert(IsValid);
            var size = _rows * _columns;
            var ret = _cuda.Sqrt(_data, size, valueAdjustment);
            return new GpuMatrix(_cuda, _rows, _columns, ret);
        }

        public IMatrix Subtract(IMatrix matrix)
        {
            Debug.Assert(IsValid && matrix.IsValid);
            var other = (GpuMatrix)matrix;
            Debug.Assert(other._rows == _rows && other._columns == _columns);

            var ret = new CudaDeviceVariable<float>(_data.Size);
            ret.CopyToDevice(_data);
            _cuda.Blas.Axpy(-1.0f, other._data, 1, ret, 1);
            return new GpuMatrix(_cuda, _rows, _columns, ret);
        }

        public void SubtractInPlace(IMatrix matrix, float coefficient1 = 1, float coefficient2 = 1)
        {
            Debug.Assert(IsValid && matrix.IsValid);
            var other = (GpuMatrix)matrix;
            Debug.Assert(other._rows == _rows && other._columns == _columns);

            _cuda.SubtractInPlace(_data, other._data, _rows * _columns, coefficient1, coefficient2);
        }

        public IMatrix TanhActivation()
        {
            Debug.Assert(IsValid);
            return new GpuMatrix(_cuda, _rows, _columns, _cuda.TanH(_data, _rows, _columns));
        }

        public IMatrix TanhDerivative()
        {
            Debug.Assert(IsValid);
            return new GpuMatrix(_cuda, _rows, _columns, _cuda.TanHDerivative(_data, _rows, _columns));
        }

        public IMatrix Transpose()
        {
            Debug.Assert(IsValid);
            var ret = new CudaDeviceVariable<float>(_rows * _columns);
            float alpha = 1.0f, beta = 0.0f;
            CudaBlasNativeMethods.cublasSgeam(_cuda.Blas.CublasHandle,
                Operation.Transpose,
                Operation.NonTranspose,
                _columns,
                _rows,
                ref alpha,
                _data.DevicePointer,
                _rows,
                ref beta,
                new ManagedCuda.BasicTypes.CUdeviceptr(0),
                _columns,
                ret.DevicePointer,
                _columns
            );
            return new GpuMatrix(_cuda, _columns, _rows, ret);
        }

        public IMatrix TransposeAndMultiply(IMatrix matrix)
        {
            Debug.Assert(IsValid && matrix.IsValid);
            var other = (GpuMatrix)matrix;
            Debug.Assert(_columns == other._columns);
            var ret = new CudaDeviceVariable<float>(_rows * other.RowCount);
            int rowsA = _rows, columnsArowsB = _columns, rowsB = other.RowCount;

            float alpha = 1.0f, beta = 0.0f;
            CudaBlasNativeMethods.cublasSgemm_v2(_cuda.Blas.CublasHandle,
                Operation.NonTranspose,
                Operation.Transpose,
                rowsA,
                rowsB,
                columnsArowsB,
                ref alpha,
                _data.DevicePointer,
                rowsA,
                other._data.DevicePointer,
                rowsB,
                ref beta,
                ret.DevicePointer,
                rowsA
            );
            return new GpuMatrix(_cuda, _rows, other.RowCount, ret);
        }

        public IMatrix TransposeThisAndMultiply(IMatrix matrix)
        {
            Debug.Assert(IsValid && matrix.IsValid);
            var other = (GpuMatrix)matrix;
            Debug.Assert(_rows == other._rows);

            var ret = new CudaDeviceVariable<float>(_columns * other.ColumnCount);
            int rowsA = _rows, columnsA = _columns, columnsB = other.ColumnCount, rowsB = other.RowCount;

            float alpha = 1.0f, beta = 0.0f;
            CudaBlasNativeMethods.cublasSgemm_v2(_cuda.Blas.CublasHandle,
                Operation.Transpose,
                Operation.NonTranspose,
                columnsA,
                columnsB,
                rowsB,
                ref alpha,
                _data.DevicePointer,
                rowsA,
                other._data.DevicePointer,
                rowsB,
                ref beta,
                ret.DevicePointer,
                columnsA
            );
            return new GpuMatrix(_cuda, _columns, other.ColumnCount, ret);
        }

        public void UpdateColumn(int index, IIndexableVector vector, int rowIndex)
        {
            Debug.Assert(IsValid);

            // TODO: use faster BLAS routine
            for (var i = 0; i < vector.Count; i++)
                _data[index * _rows + rowIndex + i] = vector[i];
        }

        public void UpdateRow(int index, IIndexableVector vector, int columnIndex)
        {
            Debug.Assert(IsValid);

            // TODO: use faster BLAS routine
            for (var i = 0; i < vector.Count; i++)
                _data[(columnIndex+i) * _rows + index] = vector[i];
        }

        public FloatArray[] Data
        {
            get
            {
                return AsIndexable().Data;
            }

            set
            {
                Debug.Assert(IsValid);
                var buffer = new float[_rows * _columns];
                _data.CopyToHost(buffer);

                var rowCount = value.Length;
                for (var i = 0; i < rowCount && i < _rows; i++) {
                    var row = value[i];
                    if (row.Data != null) {
                        var data2 = row.Data;
                        var columnCount = data2.Length;
                        for (var j = 0; j < columnCount && j < _columns; j++) {
                            buffer[j * _rows + i] = data2[j];
                        }
                    }
                }

                _data.CopyToDevice(buffer);
            }
        }

        public IMatrix Multiply(IVector vector)
        {
            Debug.Assert(IsValid && vector.IsValid);
            using (var column = vector.ToColumnMatrix())
                return Multiply(column);
        }

        public SingularValueDecomposition Svd()
        {
            Debug.Assert(IsValid);
            var solver = _cuda.Solver;

            // find the size of the required buffer
            var bufferSize = solver.GesvdBufferSizeFloat(_rows, _columns);
            var mn = Math.Min(_rows, _columns);

            // allocate output buffers
            var s = new CudaDeviceVariable<float>(mn);
            var u = new CudaDeviceVariable<float>(_rows * _rows);
            var vt = new CudaDeviceVariable<float>(_columns * _columns);

            // call cusolver to find the SVD
            try {
                using (var buffer = new CudaDeviceVariable<float>(bufferSize))
                using (var devInfo = new CudaDeviceVariable<int>(1))
                using (var rwork = new CudaDeviceVariable<float>(mn))
                using (var a = new CudaDeviceVariable<float>(_rows * _columns)) {
                    a.CopyToDevice(_data);
                    solver.Gesvd('A', 'A', _rows, _columns, a, _rows, s, u, _rows, vt, _columns, buffer, bufferSize, rwork, devInfo);
                    return new SingularValueDecomposition(
                        new GpuMatrix(_cuda, _rows, _rows, u),
                        new GpuVector(_cuda, mn, s),
                        new GpuMatrix(_cuda, _columns, _columns, vt)
                    );
                }
            }catch {
                s.Dispose();
                u.Dispose();
                vt.Dispose();
                throw;
            }
        }

        public IVector ConvertInPlaceToVector()
        {
            Debug.Assert(IsValid);
            _shouldDispose = false;
            return new GpuVector(_cuda, _rows * _columns, _data, false);
        }

        public I3DTensor MaxPool(int filterDepth, List<int[]> indexList)
        {
            throw new NotImplementedException();
        }

        public IMatrix ReverseMaxPool(IMatrix error, int size, int filterSize, int filterDepth, IReadOnlyList<int[]> indexList)
        {
            throw new NotImplementedException();
        }
    }
}
