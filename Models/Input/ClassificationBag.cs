﻿using BrightWire.Models.Output;
using BrightWire.TabularData;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BrightWire.Models.Input
{
    /// <summary>
    /// A classification bag is a sparse data structure that contains multiple indexed classifications
    /// </summary>
    [ProtoContract]
    public class ClassificationBag
    {
        /// <summary>
        /// The array of classifications
        /// </summary>
        [ProtoMember(1)]
        public IndexedClassification[] Classification { get; set; }

        /// <summary>
        /// Evaluates the classifier against each labelled classification
        /// </summary>
        /// <param name="classifier">The classifier to evaluate</param>
        /// <returns></returns>
        public IReadOnlyList<ClassificationResult> Classify(IIndexBasedClassifier classifier)
        {
            return Classification
                .Select(d => new ClassificationResult(classifier.Classify(d.Data).First(), d.Name))
                .ToList()
            ;
        }

        /// <summary>
        /// Converts the indexed classifications to sparse vectors
        /// </summary>
        /// <param name="groupByClassification">True to group by classification (i.e convert the bag to a set)</param>
        public SparseVectorClassificationSet ConvertToSparseVectors(bool groupByClassification)
        {
            if (groupByClassification) {
                return new SparseVectorClassificationSet {
                    Classification = Classification
                    .GroupBy(c => c.Name)
                        .Select(g => new SparseVectorClassification {
                            Name = g.Key,
                            Data = g.SelectMany(d => d.Data)
                                .GroupBy(d => d)
                                .Select(g2 => new WeightedIndex {
                                    Index = g2.Key,
                                    Weight = g2.Count()
                                })
                                .ToArray()
                        })
                        .ToArray()
                };
            }else {
                return new SparseVectorClassificationSet {
                    Classification = Classification
                        .Select(c => new SparseVectorClassification {
                            Name = c.Name,
                            Data = c.Data
                                .GroupBy(d => d)
                                .Select(g2 => new WeightedIndex {
                                    Index = g2.Key,
                                    Weight = g2.Count()
                                })
                                .ToArray()
                        })
                        .ToArray()
                };
            }
        }
    }
}
