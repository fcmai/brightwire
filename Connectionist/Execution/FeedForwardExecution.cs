﻿using BrightWire.Connectionist.Execution.Layer;
using BrightWire.Helper;
using BrightWire.Models.Output;
using MathNet.Numerics.LinearAlgebra.Single;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BrightWire.Connectionist.Execution
{
    internal class FeedForwardExecution : IStandardExecution
    {
        readonly ILinearAlgebraProvider _lap;
        readonly IReadOnlyList<StandardFeedForward> _layer;

        public FeedForwardExecution(ILinearAlgebraProvider lap, IReadOnlyList<StandardFeedForward> layer)
        {
            _lap = lap;
            _layer = layer;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing) {
                foreach (var item in _layer)
                    item.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public IVector Execute(float[] inputData)
        {
            using (var curr = _lap.Create(inputData))
                return Execute(curr);
        }

        void _Execute(IDisposableMatrixExecutionLine m)
        {
            foreach (var layer in _layer) {
                layer.Activate(m);
            }
        }

        void _Execute(IDisposableMatrixExecutionLine m, int layerDepth)
        {
            foreach (var layer in _layer.Take(layerDepth)) {
                layer.Activate(m);
            }
        }

        public IVector Execute(IVector inputData)
        {
            using (var m = new DisposableMatrixExecutionLine(inputData.ToRowMatrix())) {
                _Execute(m);
                return m.Current.Row(0);
            }
        }

        public IVector Execute(IVector inputData, int depth)
        {
            using (var m = new DisposableMatrixExecutionLine(inputData.ToRowMatrix())) {
                _Execute(m, depth);
                return m.Current.Row(0);
            }
        }

        public IMatrix Execute(IMatrix inputData)
        {
            using (var m = new DisposableMatrixExecutionLine()) {
                _Execute(m);
                return m.Pop();
            }
        }

        public IReadOnlyList<WeightedClassification> GetWeightedClassifications(float[] data, Dictionary<int, string> classificationTable)
        {
            return Execute(data).Data.Data
                .Select((v, i) => new WeightedClassification(classificationTable[i], v))
                .ToList()
            ;
        }
    }
}
