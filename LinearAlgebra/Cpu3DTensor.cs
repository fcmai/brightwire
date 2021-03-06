﻿using MathNet.Numerics.LinearAlgebra.Single;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Diagnostics;

namespace BrightWire.LinearAlgebra
{
    internal class Cpu3DTensor : IIndexable3DTensor
    {
        readonly CpuMatrix[] _data;
        readonly int _rows, _columns, _depth;

        public Cpu3DTensor(int rows, int columns, int depth)
        {
            _rows = rows;
            _columns = columns;
            _depth = depth;
            _data = Enumerable.Range(0, depth)
                .Select(i => new CpuMatrix(DenseMatrix.Create(_rows, _columns, 0f)))
                .ToArray()
            ;
        }

        public Cpu3DTensor(IReadOnlyList<IMatrix> matrixList)
        {
            var first = matrixList.First();
            Debug.Assert(matrixList.All(m => m.RowCount == first.RowCount && m.ColumnCount == first.ColumnCount));
            _rows = first.RowCount;
            _columns = first.ColumnCount;
            _depth = matrixList.Count;
            _data = matrixList.Cast<CpuMatrix>().ToArray();
        }

        void IDisposable.Dispose()
        {
            // nop
        }

        public float this[int row, int column, int depth]
        {
            get
            {
                return _data[depth][row, column];
            }

            set
            {
                _data[depth][row, column] = value;
            }
        }

        public IReadOnlyList<IIndexableMatrix> Data
        {
            get
            {
                return _data;
            }
        }

        public int Depth
        {
            get
            {
                return _depth;
            }
        }

        public int RowCount
        {
            get
            {
                return _rows;
            }
        }

        public int ColumnCount
        {
            get
            {
                return _columns;
            }
        }

        public IMatrix GetDepthSlice(int depth)
        {
            return _data[depth];
        }

        public IIndexable3DTensor AsIndexable()
        {
            return this;
        }

        public I3DTensor AddPadding(int padding)
        {
            var newRows = _rows + padding * 2;
            var newColumns = _columns + padding * 2;
            var ret = new Cpu3DTensor(newRows, newColumns, _depth);
            for(var k = 0; k < _depth; k++) {
                for (var j = 0; j < newRows; j++) {
                    for (var i = 0; i < newColumns; i++) {
                        if (i < padding || j < padding)
                            continue;
                        else if (i >= newRows - padding || j >= newColumns - padding)
                            continue;
                        ret[i, j, k] = this[i - padding, j - padding, k];
                    }
                }
            }
            return ret;
        }

        public I3DTensor RemovePadding(int padding)
        {
            var newRows = _rows - padding * 2;
            var newColumns = _columns - padding * 2;
            var ret = new Cpu3DTensor(newRows, newColumns, _depth);
            for (var k = 0; k < _depth; k++) {
                for (var j = 0; j < newRows; j++) {
                    for (var i = 0; i < newColumns; i++) {
                        ret[i, j, k] = this[i + padding, j + padding, k];
                    }
                }
            }
            return ret;
        }

        public IMatrix Im2Col(int filterWidth, int filterHeight, int stride)
        {
            int xOffset = 0, yOffset = 0;
            var rowList = new List<List<float>>();

            while (yOffset <= _rows - filterHeight) {
                var row = new List<float>();
                for (var k = 0; k < _depth; k++) {
                    for (var j = 0; j < filterHeight; j++) {
                        for (var i = 0; i < filterWidth; i++) {
                            row.Add(this[yOffset + j, xOffset + i, k]);
                        }
                    }
                }
                rowList.Add(row);

                // move the window
                xOffset += stride;
                if (xOffset > _columns - filterWidth) {
                    xOffset = 0;
                    yOffset += stride;
                }
            }
            var firstRow = rowList.First();
            return new CpuMatrix(DenseMatrix.Create(rowList.Count, firstRow.Count, (i, j) => rowList[i][j]));
        }

        public IVector ConvertToVector()
        {
            var vectorList = _data.Select(m => m.ConvertInPlaceToVector().AsIndexable()).ToArray();
            var size = _rows * _columns;
            var ret = DenseVector.Create(_depth * size, i => {
                var offset = i / size;
                var index = i % size;
                return vectorList[offset][index];
            });
            return new CpuVector(ret);
        }

        public IMatrix ConvertToMatrix()
        {
            var ret = DenseMatrix.Create(_rows * _columns, _depth, (i, j) => {
                var matrix = _data[j];
                var x = i / _columns;
                var y = i % _columns;
                return matrix[x, y];
            });
            return new CpuMatrix(ret);
        }

        public I3DTensor MaxPool(int filterWidth, int filterHeight, int stride, List<Dictionary<Tuple<int, int>, Tuple<int, int>>> indexPosList)
        {
            var newColumns = (ColumnCount - filterWidth) / stride + 1;
            var newRows = (RowCount - filterHeight) / stride + 1;
            var ret = new List<CpuMatrix>();

            for(var k = 0; k < Depth; k++) {
                var matrix = _data[k];
                var indexPos = new Dictionary<Tuple<int, int>, Tuple<int, int>>();
                var layer = new CpuMatrix(DenseMatrix.Create(newRows, newColumns, 0f));
                for(var x = 0; x < newColumns; x++) {
                    for(var y = 0; y < newRows; y++) {
                        var xOffset = x * stride;
                        var yOffset = y * stride;
                        var maxVal = float.MinValue;
                        var bestX = xOffset;
                        var bestY = yOffset;
                        for(var i = 0; i < filterWidth; i++) {
                            for (var j = 0; j < filterHeight; j++) {
                                var xPos = xOffset + i;
                                var yPos = yOffset + j;
                                var val = matrix[yPos, xPos];
                                if(val > maxVal) {
                                    bestX = xPos;
                                    bestY = yPos;
                                    maxVal = val;
                                }
                            }
                        }
                        indexPos[Tuple.Create(bestX, bestY)] = Tuple.Create(x, y);
                        layer[y, x] = maxVal;
                    }
                }
                if(indexPosList != null)
                    indexPosList.Add(indexPos);
                ret.Add(layer);
            }
            return new Cpu3DTensor(ret);
        }
    }
}
