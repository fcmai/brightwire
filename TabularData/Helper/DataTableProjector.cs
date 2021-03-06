﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BrightWire.TabularData.Helper
{
    internal class DataTableProjector : IRowProcessor
    {
        readonly IRowProcessor _destination;
        readonly IReadOnlyList<int> _validColumn;
        readonly RowConverter _rowConverter = new RowConverter();

        class ProjectedRow : IRow
        {
            readonly IReadOnlyList<object> _data;
            readonly RowConverter _rowConverter;

            public ProjectedRow(IReadOnlyList<object> data, RowConverter rowConverter)
            {
                _data = data;
                _rowConverter = rowConverter;
            }

            public IReadOnlyList<object> Data
            {
                get
                {
                    return _data;
                }
            }

            public T GetField<T>(int index)
            {
                return _rowConverter.GetField<T>(_data, index);
            }

            public IReadOnlyList<T> GetFields<T>(IReadOnlyList<int> indices)
            {
                var ret = new T[indices.Count];
                for (int i = 0, len = indices.Count; i < len; i++)
                    ret[i] = _rowConverter.GetField<T>(_data, i);
                return ret;
            }
        }

        public DataTableProjector(IRowProcessor destination, IEnumerable<int> columns)
        {
            _destination = destination;
            _validColumn = columns.ToList();
        }

        public bool Process(IRow row)
        {
            var data = row.Data;
            var newRow = new List<object>();
            foreach(var item in _validColumn)
                newRow.Add(data[item]);
            return _destination.Process(new ProjectedRow(newRow, _rowConverter));
        }

        public static IDataTable Project(IDataTable table, IEnumerable<int> columns, Stream output = null)
        {
            var validColumn = new HashSet<int>(columns);
            var writer = new DataTableWriter(table.Columns.Select((c, i) => Tuple.Create(c, i)).Where(c => validColumn.Contains(c.Item2)).Select(c => c.Item1), output);
            var projector = new DataTableProjector(writer, columns);
            table.Process(projector);
            return writer.GetDataTable();
        }

        public static IDataTable Project(IDataTable table, params int[] columns)
        {
            return Project(table, columns);
        }
    }
}
