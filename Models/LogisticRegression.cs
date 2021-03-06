﻿using BrightWire.Linear;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Text;

namespace BrightWire.Models
{
    /// <summary>
    /// A logistic regression model
    /// </summary>
    [ProtoContract]
    public class LogisticRegression
    {
        /// <summary>
        /// The model parameters
        /// </summary>
        [ProtoMember(1)]
        public FloatArray Theta { get; set; }

        /// <summary>
        /// Creates a classifier from this model
        /// </summary>
        /// <param name="lap">Linear algebra provider</param>
        public ILogisticRegressionClassifier CreatePredictor(ILinearAlgebraProvider lap)
        {
            return new LogisticRegressionPredictor(lap, lap.Create(Theta.Data));
        }
    }
}
