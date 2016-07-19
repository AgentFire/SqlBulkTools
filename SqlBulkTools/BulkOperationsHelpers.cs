﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

[assembly: InternalsVisibleTo("SqlBulkTools.UnitTests")]
[assembly: InternalsVisibleTo("SqlBulkTools.IntegrationTests")]
namespace SqlBulkTools
{   
    internal class BulkOperationsHelpers
    {
        public string BuildCreateTempTable(HashSet<string> columns, DataTable schema)
        {
            Dictionary<string, string> actualColumns = new Dictionary<string, string>();
            Dictionary<string, string> actualColumnsMaxCharLength = new Dictionary<string, string>();

            
            foreach (DataRow row in schema.Rows)
            {
                //row["TABLE_NAME"];
                //row["TABLE_SCHEMA"];
                actualColumns.Add(row["COLUMN_NAME"].ToString(), row["DATA_TYPE"].ToString());
                actualColumnsMaxCharLength.Add(row["COLUMN_NAME"].ToString(),
                    row["CHARACTER_MAXIMUM_LENGTH"].ToString());
            }


            StringBuilder command = new StringBuilder();

            command.Append("CREATE TABLE #TmpTable(");

            List<string> paramList = new List<string>();

            foreach (var keyValuePair in columns.ToList())
            {
                string columnType;
                if (actualColumns.TryGetValue(keyValuePair, out columnType))
                {
                    if (columnType == "varchar" || columnType == "nvarchar")
                    {
                        string maxCharLength;
                        if (actualColumnsMaxCharLength.TryGetValue(keyValuePair, out maxCharLength))
                        {
                            if (maxCharLength == "-1")
                                maxCharLength = "max";

                            columnType = columnType + "(" + maxCharLength + ")";
                        }
                    }
                }

                paramList.Add(keyValuePair + " " + columnType);
            }

            string paramListConcatenated = string.Join(", ", paramList);

            command.Append(paramListConcatenated);
            command.Append(")");

            return command.ToString();
        }

        public string RemoveSchemaFromTable(string tableName)
        {
            Regex regex = new Regex(@"\[.*\]\.");
            Match match = regex.Match(tableName);
            if (match.Success)
            {
                return tableName.Remove(0, match.Value.Length);
            }

            return tableName;

        }

        public string BuildJoinConditionsForUpdateOrInsert(string[] updateOn, string sourceAlias, string targetAlias)
        {
            StringBuilder command = new StringBuilder();

            command.Append("ON " + targetAlias + "." + updateOn[0] + " = " + sourceAlias + "." + updateOn[0] + " ");

            if (updateOn.Length > 1)
            {
                // Start from index 1 to just append "AND" conditions
                for (int i = 1; i < updateOn.Length; i++)
                {
                    command.Append("AND " + targetAlias + "." + updateOn[i] + " = " + sourceAlias + "." + updateOn[i] + " ");
                }
            }

            return command.ToString();
        }

        public string BuildUpdateSet(HashSet<string> columns, string sourceAlias, string targetAlias, string identityColumn)
        {
            StringBuilder command = new StringBuilder();
            List<string> paramsSeparated = new List<string>();

            command.Append("UPDATE SET ");

            foreach (var column in columns.ToList())
            {
                if (identityColumn != null && column != identityColumn || identityColumn == null)
                {
                    paramsSeparated.Add(targetAlias + "." + column + " = " + sourceAlias + "." + column);
                }             
            }

            command.Append(string.Join(", ", paramsSeparated) + " ");

            return command.ToString();
        }

        public string BuildInsertSet(HashSet<string> columns, string sourceAlias)
        {
            StringBuilder command = new StringBuilder();
            List<string> insertColumns = new List<string>();
            List<string> values = new List<string>();

            command.Append("INSERT (");

            foreach (var column in columns.ToList())
            {
                insertColumns.Add(column);
                values.Add(sourceAlias + "." + column);
            }

            command.Append(string.Join(", ", insertColumns));
            command.Append(") values (");
            command.Append(string.Join(", ", values));
            command.Append(")");

            return command.ToString();
        }

        public string GetPropertyName(Expression method)
        {
            LambdaExpression lambda = method as LambdaExpression;
            if (lambda == null)
                throw new ArgumentNullException("method");

            MemberExpression memberExpr = null;

            if (lambda.Body.NodeType == ExpressionType.Convert)
            {
                memberExpr =
                    ((UnaryExpression)lambda.Body).Operand as MemberExpression;
            }
            else if (lambda.Body.NodeType == ExpressionType.MemberAccess)
            {
                memberExpr = lambda.Body as MemberExpression;
            }

            if (memberExpr == null)
                throw new ArgumentException("method");

            return memberExpr.Member.Name;
        }

        public DataTable ToDataTable<T>(IEnumerable<T> items, HashSet<string> columns, Dictionary<string, string> columnMappings, List<string> matchOnColumns = null)
        {
            DataTable dataTable = new DataTable(typeof(T).Name);

            if (matchOnColumns != null)
            {
                columns = CheckForAdditionalColumns(columns, matchOnColumns);
            }

            //Get all the properties
            PropertyInfo[] props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var column in columns.ToList())
            {
                if (columnMappings.ContainsKey(column))
                {
                    dataTable.Columns.Add(columnMappings[column]);
                }

                else
                    dataTable.Columns.Add(column);
            }

            foreach (T item in items)
            {
                var values = new List<object>();

                foreach (var column in columns.ToList())
                {
                    for (int i = 0; i < props.Length; i++)
                    {
                        if (props[i].Name == column && item != null)
                            values.Add(props[i].GetValue(item, null));
                    }

                }

                dataTable.Rows.Add(values.ToArray());

            }
            return dataTable;
        }

        /// <summary>
        /// If there are MatchOnColumns that don't exist in columns, add to columns.
        /// </summary>
        /// <param name="columns"></param>
        /// <param name="MatchOnColumns"></param>
        /// <param name="matchOnColumns"></param>
        /// <returns></returns>
        public HashSet<string> CheckForAdditionalColumns(HashSet<string> columns, List<string> matchOnColumns)
        {
            foreach (var col in matchOnColumns)
            {
                if (!columns.Contains(col))
                {
                    columns.Add(col);
                }
            }

            return columns;
        }

        public void DoColumnMappings(Dictionary<string, string> columnMappings, HashSet<string> columns,
        List<string> updateOnList)
        {
            if (columnMappings.Count > 0)
            {
                foreach (var column in columnMappings)
                {
                    if (columns.Contains(column.Key))
                    {
                        columns.Remove(column.Key);
                        columns.Add(column.Value);
                    }

                    for (int i = 0; i < updateOnList.ToArray().Length; i++)
                    {
                        if (updateOnList[i] == column.Key)
                        {
                            updateOnList[i] = column.Value;
                        }
                    }
                }
            }
        }

        public void DoColumnMappings(Dictionary<string, string> columnMappings, HashSet<string> columns)
        {
            if (columnMappings.Count > 0)
            {
                foreach (var column in columnMappings)
                {
                    if (columns.Contains(column.Key))
                    {
                        columns.Remove(column.Key);
                        columns.Add(column.Value);
                    }
                }
            }
        }

        /// <summary>
        /// Advanced Settings for SQLBulkCopy class. 
        /// </summary>
        /// <param name="bulkcopy"></param>
        /// <param name="bulkCopyEnableStreaming"></param>
        /// <param name="bulkCopyBatchSize"></param>
        /// <param name="bulkCopyNotifyAfter"></param>
        /// <param name="bulkCopyTimeout"></param>
        public void SetSqlBulkCopySettings(SqlBulkCopy bulkcopy, bool bulkCopyEnableStreaming, int? bulkCopyBatchSize, int? bulkCopyNotifyAfter, int bulkCopyTimeout)
        {
            bulkcopy.EnableStreaming = bulkCopyEnableStreaming;

            if (bulkCopyBatchSize.HasValue)
            {
                bulkcopy.BatchSize = bulkCopyBatchSize.Value;
            }

            if (bulkCopyNotifyAfter.HasValue)
            {
                bulkcopy.NotifyAfter = bulkCopyNotifyAfter.Value;
            }

            bulkcopy.BulkCopyTimeout = bulkCopyTimeout;
        }

        public void ValidateConnection(string connectionName)
        {
            if (ConfigurationManager.ConnectionStrings[connectionName] == null)
                throw new InvalidOperationException("Connection name not found. Recheck your configuration and provide the a valid connection name.");
        }

        /// <summary>
        /// This is used only for the BulkInsert method at this time.  
        /// </summary>
        /// <param name="bulkCopy"></param>
        /// <param name="columns"></param>
        /// <param name="customColumnMappings"></param>
        public void MapColumns(SqlBulkCopy bulkCopy, HashSet<string> columns, Dictionary<string, string> customColumnMappings)
        {

            foreach (var column in columns.ToList())
            {
                string mapping;

                if (customColumnMappings.TryGetValue(column, out mapping))
                {
                    bulkCopy.ColumnMappings.Add(mapping, mapping);
                }

                else
                    bulkCopy.ColumnMappings.Add(column, column);
            }

        }

        /// <summary>
        /// Gets schema information for a table. Used to get SQL type of property. 
        /// </summary>
        /// <param name="conn"></param>
        /// <param name="schema"></param>
        /// <param name="tableName"></param>
        /// <returns></returns>
        public DataTable GetSchema(SqlConnection conn, string schema, string tableName)
        {
            string[] restrictions = new string[4];
            restrictions[0] = conn.Database;
            restrictions[1] = schema ?? null;
            restrictions[2] = RemoveSchemaFromTable(tableName);
            var dtCols = conn.GetSchema("Columns", restrictions);

            if (dtCols.Rows.Count == 0 && schema != null) throw new InvalidOperationException("Table name " + tableName + " with schema " + schema + " not found. Check your setup and try again.");
            if (dtCols.Rows.Count == 0) throw new InvalidOperationException("Table name " + tableName + " not found. Check your setup and try again.");
            return dtCols;
        }
    }
}