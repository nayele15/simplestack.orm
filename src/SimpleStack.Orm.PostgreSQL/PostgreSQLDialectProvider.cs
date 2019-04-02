﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;
using Dapper;
using Npgsql;
using SimpleStack.Orm.Expressions;

namespace SimpleStack.Orm.PostgreSQL
{
    /// <summary>A postgre SQL dialect provider.</summary>
    public class PostgreSQLDialectProvider : DialectProviderBase
    {
        /// <summary>The text column definition.</summary>
        private const string TextColumnDefinition = "text";

        /// <summary>
        ///     Prevents a default instance of the NServiceKit.OrmLite.PostgreSQL.PostgreSQLDialectProvider
        ///     class from being created.
        /// </summary>
        public PostgreSQLDialectProvider()
        {
            AutoIncrementDefinition            = "";
            IntColumnDefinition                = "integer";
            BoolColumnDefinition               = "boolean";
            TimeColumnDefinition               = "time";
            DateTimeColumnDefinition           = "timestamp";
            DecimalColumnDefinition            = "numeric(38,6)";
            GuidColumnDefinition               = "uuid";
            ParamPrefix                        = ":";
            BlobColumnDefinition               = "bytea";
            RealColumnDefinition               = "double precision";
            StringLengthColumnDefinitionFormat = TextColumnDefinition;
            //there is no "n"varchar in postgres. All strings are either unicode or non-unicode, inherited from the database.
            StringLengthUnicodeColumnDefinitionFormat    = "character varying({0})";
            StringLengthNonUnicodeColumnDefinitionFormat = "character varying({0})";
            InitColumnTypeMap();
            base.SelectIdentitySql = "SELECT LASTVAL()";
            NamingStrategy         = new PostgreSqlNamingStrategy();
            DbTypeMap.Set(DbType.Time, "Interval");
            DbTypeMap.Set(DbType.Time, "Interval");
            DefaultStringLength = 255;
        }

        /// <summary>Creates a connection.</summary>
        /// <param name="connectionString">The connection string.</param>
        /// <param name="options">         Options for controlling the operation.</param>
        /// <returns>The new connection.</returns>
        public override DbConnection CreateIDbConnection(string connectionString)
        {
            return new NpgsqlConnection(connectionString);
        }

        /// <summary>Gets column definition.</summary>
        /// <param name="fieldName">    Name of the field.</param>
        /// <param name="fieldType">    Type of the field.</param>
        /// <param name="isPrimaryKey"> true if this object is primary key.</param>
        /// <param name="autoIncrement">true to automatically increment.</param>
        /// <param name="isNullable">   true if this object is nullable.</param>
        /// <param name="fieldLength">  Length of the field.</param>
        /// <param name="scale">        The scale.</param>
        /// <param name="defaultValue"> The default value.</param>
        /// <returns>The column definition.</returns>
        public override string GetColumnDefinition(
            string fieldName,
            Type fieldType,
            bool isPrimaryKey,
            bool autoIncrement,
            bool isNullable,
            int? fieldLength,
            int? scale,
            string defaultValue)
        {
            string fieldDefinition = null;
            if (fieldType == typeof(string))
            {
                if (fieldLength != null)
                {
                    fieldDefinition = string.Format(StringLengthColumnDefinitionFormat, fieldLength);
                }
                else
                {
                    fieldDefinition = TextColumnDefinition;
                }
            }
            else
            {
                if (autoIncrement)
                {
                    if (fieldType == typeof(long))
                    {
                        fieldDefinition = "bigserial";
                    }
                    else if (fieldType == typeof(int))
                    {
                        fieldDefinition = "serial";
                    }
                }
                else
                {
                    fieldDefinition = GetColumnTypeDefinition(fieldType, fieldName, fieldLength);
                }
            }

            var sql = new StringBuilder();
            sql.AppendFormat("{0} {1}", GetQuotedColumnName(fieldName), fieldDefinition);
            if (isPrimaryKey && autoIncrement)
            {
                sql.Append(" PRIMARY KEY");
            }
            else
            {
                if (isNullable)
                {
                    sql.Append(" NULL");
                }
                else
                {
                    sql.Append(" NOT NULL");
                }
            }

            if (!string.IsNullOrEmpty(defaultValue))
            {
                sql.AppendFormat(DefaultValueFormat, defaultValue);
            }

            return sql.ToString();
        }

		/// <summary>Expression visitor.</summary>
		/// <typeparam name="T">Generic type parameter.</typeparam>
		/// <returns>A SqlExpressionVisitor&lt;T&gt;</returns>
		public override SqlExpressionVisitor<T> ExpressionVisitor<T>()
		{
			return new PostgreSQLExpressionVisitor<T>(this);
		}

		/// <summary>Query if 'dbCmd' does table exist.</summary>
		/// <param name="connection">    The database command.</param>
		/// <param name="tableName">Name of the table.</param>
		/// <returns>true if it succeeds, false if it fails.</returns>
		public override bool DoesTableExist(IDbConnection connection, string tableName)
		{
			var result = connection.ExecuteScalar<long>(
				"SELECT COUNT(*) FROM pg_class WHERE relname = :table", 
				new { table = tableName});

			return result > 0;
		}

		/// <summary>Gets quoted table name.</summary>
		/// <param name="modelDef">The model definition.</param>
		/// <returns>The quoted table name.</returns>
		public override string GetQuotedTableName(ModelDefinition modelDef)
		{
			if (!modelDef.IsInSchema)
			{
				return base.GetQuotedTableName(modelDef);
			}
			string escapedSchema = modelDef.Schema.Replace(".", "\".\"");
			return string.Format("\"{0}\".\"{1}\"", escapedSchema, base.NamingStrategy.GetTableName(modelDef.ModelName));
		}

		/// <summary>
		/// based on Npgsql2's source: Npgsql2\src\NpgsqlTypes\NpgsqlTypeConverters.cs.
		/// </summary>
		/// <param name="NativeData">.</param>
		/// <returns>A binary represenation of this object.</returns>
		/// ### <param name="TypeInfo">        .</param>
		/// ### <param name="ForExtendedQuery">.</param>
		internal static String ToBinary(Object NativeData)
		{
			Byte[] byteArray = (Byte[])NativeData;
			StringBuilder res = new StringBuilder(byteArray.Length * 5);
			foreach (byte b in byteArray)
				if (b >= 0x20 && b < 0x7F && b != 0x27 && b != 0x5C)
					res.Append((char)b);
				else
					res.Append("\\\\")
						.Append((char)('0' + (7 & (b >> 6))))
						.Append((char)('0' + (7 & (b >> 3))))
						.Append((char)('0' + (7 & b)));
			return res.ToString();
		}

		public override string GetDropTableStatement(ModelDefinition modelDef)
		{
			return "DROP TABLE " + GetQuotedTableName(modelDef) + " CASCADE";
		}

		public override string GetDatePartFunction(string name, string quotedColName)
		{
			return $"date_part('{name.ToLower()}', {quotedColName})";
		}

        private class PostgreSQLColumnDefinition
        {
            public string Column_Name { get; set; }
            public string Is_Nullable { get; set; }
            public string Character_Maximum_Length { get; set; }


        }

        private class PostgreSqlTableDefinition
        {
            public string Table_Name { get; set; }
        }

        public override IEnumerable<ColumnDefinition> GetTableColumnDefinitions(IDbConnection connection, string tableName, string schemaName = null)
        {
            string sqlQuery =
                "SELECT * FROM information_schema.columns WHERE table_schema = '@SchemaName' AND table_name = '@TableName';";
            foreach (var column in connection.Query<PostgreSQLColumnDefinition>(sqlQuery,
                new { TableName = tableName, SchemaName = schemaName }))
            {
                yield return new ColumnDefinition
                {
                    FieldLength = int.Parse(column.Character_Maximum_Length),
                    Nullable = column.Is_Nullable == "YES",
                    Name = column.Column_Name
                };
            }


        }

        public override IEnumerable<TableDefinition> GetTableDefinitions(IDbConnection connection, string dbName, string schemaName)
        {
            string sqlQuery = "SELECT * FROM information_schema.tables WHERE table_schema = '@SchemaName';";
            foreach (var table in connection.Query<PostgreSqlTableDefinition>(sqlQuery, new { SchemaName = schemaName }))
            {
                yield return new TableDefinition
                {
                    Name = table.Table_Name
                };
            }
        }
    }
}
