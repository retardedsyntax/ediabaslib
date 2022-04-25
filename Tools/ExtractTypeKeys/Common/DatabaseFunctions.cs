﻿using System;
using System.Data.SQLite;

public static class DatabaseFunctions
{
    public const string SqlTitleItems =
        "TITLE_DEDE, TITLE_ENGB, TITLE_ENUS, " +
        "TITLE_FR, TITLE_TH, TITLE_SV, " +
        "TITLE_IT, TITLE_ES, TITLE_ID, " +
        "TITLE_KO, TITLE_EL, TITLE_TR, " +
        "TITLE_ZHCN, TITLE_RU, TITLE_NL, " +
        "TITLE_PT, TITLE_ZHTW, TITLE_JA, " +
        "TITLE_CSCZ, TITLE_PLPL";

    public static string GetNodeClassId(SQLiteConnection mDbConnection, string nodeClassName)
    {
        string result = string.Empty;
        string sql = string.Format(@"SELECT ID FROM XEP_NODECLASSES WHERE NAME = '{0}'", nodeClassName);
        using (SQLiteCommand command = new SQLiteCommand(sql, mDbConnection))
        {
            using (SQLiteDataReader reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    result = reader["ID"].ToString();
                }
            }
        }

        return result;
    }
}