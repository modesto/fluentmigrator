﻿using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FluentMigrator.T4
{
    public class CodeGenerator
    {
        private readonly TextWriter writer;

        private readonly Action<string> warning;

        public CodeGenerator(string connectionString, string providerName, TextWriter writer, Action<string> Warning)
        {
            this.connectionString = connectionString;
            this.providerName = providerName;
            this.writer = writer;
            warning = Warning;
        }

        private readonly string connectionString;

        private readonly string providerName;

        private string classPrefix = "";

        private string classSuffix = "";

        private readonly string SchemaName = null;

        private bool includeViews = false;


        private static string ZapPassword(string connectionString)
        {
            var rx = new Regex("password=.*;", RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.IgnoreCase);
            return rx.Replace(connectionString, "password=**zapped**;");
        }

        public string GetCurrentTimeStamp()
        {
            return DateTime.Now.ToString("yyyyMMddhhmmss");
        }

        public string GetColumnDefaultValue(Column col)
        {
            string sysType = string.Format("\"{0}\"", col.DefaultValue);
            var guid = Guid.Empty;
            switch (col.PropertyType)
            {
                case System.Data.DbType.Byte:
                case System.Data.DbType.Currency:
                case System.Data.DbType.Decimal:
                case System.Data.DbType.Double:
                case System.Data.DbType.Boolean:
                case System.Data.DbType.Int16:
                case System.Data.DbType.Int32:
                case System.Data.DbType.Int64:
                case System.Data.DbType.Single:
                case System.Data.DbType.UInt16:
                case System.Data.DbType.UInt32:
                case System.Data.DbType.UInt64:
                    sysType = col.DefaultValue.Replace("'", "").Replace("\"", "").CleanBracket();
                    break;
                case System.Data.DbType.Guid:
                    if (col.DefaultValue.IsGuid(out guid))
                    {
                        if (guid == Guid.Empty)
                            sysType = "Guid.Empty";
                        else
                            sysType = string.Format("new System.Guid(\"{0}\")", guid);                        
                    }
                    break;
                case System.Data.DbType.DateTime:
                case System.Data.DbType.DateTime2:
                case System.Data.DbType.Date:
                    if (col.DefaultValue.ToLower() == "current_time"
                        || col.DefaultValue.ToLower() == "current_date"
                        || col.DefaultValue.ToLower() == "current_timestamp")
                    {
                        sysType = "SystemMethods.CurrentDateTime";
                    }
                    else
                    {
                        sysType = "\"" + col.DefaultValue.CleanBracket() + "\"";
                    }
                    break;
                default:
                    break;
            }

            return sysType;
        }

        public Tables LoadTables()
        {
            writer.WriteLine("// This file was automatically generated by the PetaPoco T4 Template");
            writer.WriteLine("// Do not make changes directly to this file - edit the template instead");
            writer.WriteLine("// ");
            writer.WriteLine("// The following connection settings were used to generate this file");
            writer.WriteLine("// ");
            writer.WriteLine("//     Provider:               `{0}`", this.providerName);
            writer.WriteLine("//     Connection String:      `{0}`", ZapPassword(this.connectionString));
            writer.WriteLine("//     Schema:                 `{0}`", this.SchemaName);
            writer.WriteLine("//     Include Views:          `{0}`", this.includeViews);
            writer.WriteLine("");

            DbProviderFactory _factory;
            try
            {
                _factory = DbProviderFactories.GetFactory(this.providerName);
            }
            catch (Exception x)
            {
                var error = x.Message.Replace("\r\n", "\n").Replace("\n", " ");
                warning(string.Format("Failed to load provider `{0}` - {1}", this.providerName, error));
                writer.WriteLine("");
                writer.WriteLine("// -----------------------------------------------------------------------------------------");
                writer.WriteLine("// Failed to load provider `{0}` - {1}", this.providerName, error);
                writer.WriteLine("// -----------------------------------------------------------------------------------------");
                writer.WriteLine("");
                return new Tables();
            }
            writer.WriteLine("//     Factory Name:          `{0}`", _factory.GetType().Name);

            try
            {
                using (var conn = _factory.CreateConnection())
                {
                    conn.ConnectionString = this.connectionString;
                    conn.Open();

                    SchemaReader reader;

                    if (_factory.GetType().Name == "MySqlClientFactory")
                    {
                        // MySql
                        reader = new MySqlSchemaReader();
                    }
                    else if (_factory.GetType().Name == "SqlCeProviderFactory")
                    {
                        // SQL CE
                        reader = new SqlServerCeSchemaReader();
                    }
                    else if (_factory.GetType().Name == "NpgsqlFactory")
                    {
                        // PostgreSQL
                        reader = new PostGreSqlSchemaReader();
                    }
                    else if (_factory.GetType().Name == "OracleClientFactory")
                    {
                        // Oracle
                        reader = new OracleSchemaReader();
                    }
                    else if (_factory.GetType().Name == "SQLiteFactory")
                    {
                        // Sqlite
                        reader = new SqliteSchemaReader();
                    }
                    else
                    {
                        // Assume SQL Server
                        reader = new SqlServerSchemaReader();
                    }
                    reader.outer = writer;
                    Tables result = reader.ReadSchema(conn, _factory);
                    // Remove unrequired tables/views
                    for (int i = result.Count - 1; i >= 0; i--)
                    {
                        if (this.SchemaName != null && string.Compare(result[i].Schema, this.SchemaName, true) != 0)
                        {
                            result.RemoveAt(i);
                            continue;
                        }
                        if (!this.includeViews && result[i].IsView)
                        {
                            result.RemoveAt(i);
                        }
                    }
                    conn.Close();

                    var rxClean = new Regex("^(Equals|GetHashCode|GetType|ToString|repo|Save|IsNew|Insert|Update|Delete|Exists|SingleOrDefault|Single|First|FirstOrDefault|Fetch|Page|Query)$");
                    foreach (var t in result)
                    {
                        t.ClassName = this.classPrefix + t.ClassName + this.classSuffix;
                        foreach (var c in t.Columns)
                        {
                            c.PropertyName = rxClean.Replace(c.PropertyName, "_$1");

                            // Make sure property name doesn't clash with class name
                            if (c.PropertyName == t.ClassName)
                            {
                                c.PropertyName = "_" + c.PropertyName;
                            }
                        }
                    }

                    return result;
                }
            }
            catch (Exception x)
            {
                var error = x.Message.Replace("\r\n", "\n").Replace("\n", " ");
                warning(string.Format("Failed to read database schema - {0}", error));
                writer.WriteLine("");
                writer.WriteLine("// -----------------------------------------------------------------------------------------");
                writer.WriteLine("// Failed to read database schema - {0}", error);
                writer.WriteLine("// -----------------------------------------------------------------------------------------");
                writer.WriteLine("");
                return new Tables();
            }
        }

        public bool IsTableNameInList(string tableName, Tables tbls)
        {
            if (tbls == null)
            {
                return false;
            }
            foreach (var tbItem in tbls)
            {
                if (String.Equals(tbItem.Name, tableName, StringComparison.InvariantCultureIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public Table GetTableFromListByName(string tableName, Tables tbls)
        {
            if (tbls == null)
            {
                return null;
            }
            foreach (var tbItem in tbls)
            {
                if (String.Equals(tbItem.Name, tableName, StringComparison.InvariantCultureIgnoreCase))
                {
                    return tbItem;
                }
            }
            return null;
        }

        public string GetMigrationTypeFunctionForType(Column col)
        {
            var size = col.Size;
            var precision = col.Precision;
            string sizeStr = ((size == -1) ? "" : size.ToString());
            string precisionStr = ((precision == -1) ? "" : "," + precision.ToString());
            string sysType = "AsString(" + sizeStr + ")";
            switch (col.PropertyType)
            {
                case System.Data.DbType.AnsiString:
                    sysType = string.Format("AsAnsiString({0})", sizeStr);
                    break;
                case System.Data.DbType.AnsiStringFixedLength:
                    sysType = string.Format("AsFixedLengthAnsiString({0})", sizeStr);
                    break;
                case System.Data.DbType.Binary:
                    sysType = string.Format("AsBinary({0})", sizeStr);
                    break;
                case System.Data.DbType.Boolean:
                    sysType = "AsBoolean()";
                    break;
                case System.Data.DbType.Byte:
                    sysType = "AsByte()";
                    break;
                case System.Data.DbType.Currency:
                    sysType = "AsCurrency()";
                    break;
                case System.Data.DbType.Date:
                    sysType = "AsDate()";
                    break;
                case System.Data.DbType.DateTime:
                    sysType = "AsDateTime()";
                    break;
                case System.Data.DbType.Decimal:
                    sysType = string.Format("AsDecimal({0})", sizeStr + precisionStr);
                    break;
                case System.Data.DbType.Double:
                    sysType = "AsDouble()";
                    break;
                case System.Data.DbType.Guid:
                    sysType = "AsGuid()";
                    break;
                case System.Data.DbType.Int16:
                case System.Data.DbType.UInt16:
                    sysType = "AsInt16()";
                    break;
                case System.Data.DbType.Int32:
                case System.Data.DbType.UInt32:
                    sysType = "AsInt32()";
                    break;
                case System.Data.DbType.Int64:
                case System.Data.DbType.UInt64:
                    sysType = "AsInt64()";
                    break;
                case System.Data.DbType.Single:
                    sysType = "AsFloat()";
                    break;
                case System.Data.DbType.String:
                    sysType = string.Format("AsString({0})", sizeStr);
                    break;
                case System.Data.DbType.StringFixedLength:
                    sysType = string.Format("AsFixedLengthString({0})", sizeStr);
                    break;
                case null:
                    sysType = string.Format("AsCustom({0})", col.CustomType);
                    break;
                default:
                    break;
            }

            return sysType;
        }
    }
}