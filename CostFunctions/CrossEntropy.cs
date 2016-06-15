﻿using BrightWire.Helper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BrightWire.CostFunction
{
    public class CrossEntropy : ICostFunction
    {
        public float Calculate(IIndexableVector output, IIndexableVector expectedOutput)
        {
            float ret = 0;
            var len = output.Count;
            for (var i = 0; i < len; i++) {
                var a = output[i];
                var y = expectedOutput[i];
                ret += BoundMath.Constrain(-y * BoundMath.Log(a) - (1.0f - y) * BoundMath.Log(1.0f - a));
            }
            return ret / len;
        }
    }
}
