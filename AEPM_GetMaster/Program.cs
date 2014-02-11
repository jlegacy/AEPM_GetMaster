using System;
using System.Configuration;
using System.Data;
using System.Data.OleDb;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using AEPM_GetMaster.Properties;
using AEPM_GetMaster.ServiceReference1;


namespace AEPM_GetMaster
{
    public static class Program
    {

        public static void Main(string[] args)
        {

            WriteToEventLog("Calling AEPM Service...");

            var dt = new DataTable();
            var dset = new DataSet();
            Guid = args[0];

            //retrieve any records needing updating
            try
            {
              
                 using(var conn = new OleDbConnection(ConfigurationManager.AppSettings["AS400ConnectionStringDev"]))
                
                {
                    string sql = GetUnprocessMasterRecsString();
                    var objDataAdapter = new OleDbDataAdapter();
                    var cmd = new OleDbCommand(sql, conn);

                    objDataAdapter.SelectCommand = cmd;
                    objDataAdapter.SelectCommand.CommandTimeout = 0;

                    dt.Clear();
                    dset.Clear();

                    objDataAdapter.Fill(dt);
                    objDataAdapter.Fill(dset, "currentSelections");

                    var cb = new OleDbCommandBuilder(objDataAdapter);
                    AddParameters(cb);

                    //update records to 'S' for submitted
                    for (int i = 0; i < dset.Tables["currentSelections"].Rows.Count; i++)
                    {
                        dset.Tables["currentSelections"].Rows[i]["G_RETRN"] = 'S';
                    }

                    objDataAdapter.Update(dset, "currentSelections");



                    //submit records asynch
                    for (int i = 0; i < dt.Rows.Count; i++)
                    {
                        TestAsync(dt, i);
                    }


                }
            }
            catch (Exception ex)
            {
                WriteToEventLog(ex);
            }
        }

        public static string Guid { get; set; }

        public static void TestAsync(DataTable dt, int i)
        {
           
            string guidString = GetGuidString(dt, i);
            string partString = GetPartString(dt, i);

            //Under Settings, use the following links to either point to production or test
            //production = http://enmiis01.global.nmhg.corp/AEPM_services/Services.svc'
            //test = http://enmdevex.global.nmhg.corp:82/AEPM_services/Services.svc'

            IServices client = new ServicesClient();

            GetMasterResult getResult = client.GetMaster(partString);

            Type objectType = getResult.GetType();
            var xmlSerializer = new XmlSerializer(objectType);
            var memoryStream = new MemoryStream();
            using (var xmlTextWriter =
                new XmlTextWriter(memoryStream, Encoding.Default) { Formatting = Formatting.None })
            {
                xmlSerializer.Serialize(xmlTextWriter, getResult);
                memoryStream = (MemoryStream)xmlTextWriter.BaseStream;
                // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
                new UTF8Encoding().GetString(memoryStream.ToArray());
                memoryStream.Dispose();
                // return xmlText;
            }

            if (getResult.Error == null)
            {
                UpdateFoundPart(guidString, getResult);

                InsertCrossParts(guidString, getResult);
            }
            else
            {
                UpdateFoundNotPart(guidString);
            }
               
        }
        

        private static void UpdateFoundNotPart(string passedGuid)
        {
            using (var conn = new OleDbConnection(ConfigurationManager.AppSettings["AS400ConnectionStringDev"]))
            {
                string query = GetPartNotFoundUpdateMasterString();

                var cmd = new OleDbCommand(query, conn);

                cmd.Connection.Open();

                cmd.CommandText = query;

                cmd.CommandText = cmd.CommandText.Replace("@passedGuid", ConvertString(passedGuid));
                cmd.CommandText = cmd.CommandText.Replace("@retrn", ConvertString("N"));

                cmd.ExecuteNonQuery();
                cmd.Connection.Close();
            }
        }

        private static String ConvertString(String x)
        {
            String j = "'" + x + "'";
            return j;
        }

        private static String ConvertString(Boolean x)
        {
            String j = "'" + x + "'";
            return j;
        }


        private static void UpdateFoundPart(string passedGuid, GetMasterResult getResult)
        {
            using (var conn = new OleDbConnection(ConfigurationManager.AppSettings["AS400ConnectionStringDev"]))
            {
                string query = GetPartFoundUpdateMasterString();

                var cmd = new OleDbCommand(query, conn);

                cmd.Connection.Open();
                cmd.CommandText = query;

                cmd.CommandText = cmd.CommandText.Replace("@passedGuid", ConvertString(passedGuid));
                cmd.CommandText = cmd.CommandText.Replace("@usrid", ConvertString((getResult.UserID.Trim().Length > 0) ? getResult.UserID : " "));
                cmd.CommandText = cmd.CommandText.Replace("@branded", ConvertString(getResult.Branded));
                cmd.CommandText = cmd.CommandText.Replace("@comcode",
                    ConvertString((getResult.Commodity_Code.Trim().Length > 0)
                        ? getResult.Commodity_Code
                        : " "));
                cmd.CommandText = cmd.CommandText.Replace("@level", (getResult.Level).ToString(CultureInfo.InvariantCulture));
                cmd.CommandText = cmd.CommandText.Replace("@status", ConvertString((getResult.Status.Trim().Length > 0) ? getResult.Status : " "));

                cmd.CommandText = cmd.CommandText.Replace("@rtnble", ConvertString((getResult.Status.Trim().Length > 0) ? getResult.Status : " "));
                cmd.CommandText = cmd.CommandText.Replace("@tariffcd", ConvertString(getResult.Returnable));
                cmd.CommandText = cmd.CommandText.Replace("@amsc", ConvertString((getResult.AMSC.Trim().Length > 0) ? getResult.AMSC : " "));
                cmd.CommandText = cmd.CommandText.Replace("@tqty", getResult.Technical_Qty.ToString(CultureInfo.InvariantCulture));
                cmd.CommandText = cmd.CommandText.Replace("@svclife", getResult.Service_Life.ToString(CultureInfo.InvariantCulture));
                cmd.CommandText = cmd.CommandText.Replace("@pkgcode", ConvertString((getResult.Package_Code.Trim().Length > 0) ? getResult.Package_Code : " "));
                cmd.CommandText = cmd.CommandText.Replace("@info", ConvertString((getResult.Information.Trim().Length > 0) ? getResult.Information : " "));
                cmd.CommandText = cmd.CommandText.Replace("@retrn", ConvertString("R"));

                cmd.ExecuteNonQuery();
                cmd.Connection.Close();
            }
        }

        private static void InsertCrossParts(string passedGuid, GetMasterResult getResult)
        {
            foreach (CrossPart s in getResult.CrossPartList)
            {
                using (
                    var conn = new OleDbConnection(ConfigurationManager.AppSettings["AS400ConnectionStringDev"]))
                {
                    string query = GetCrossPartInsertString();

                    var cmd = new OleDbCommand(query, conn);

                    cmd.Connection.Open();
                    
                    cmd.CommandText = query;

                    cmd.CommandText = cmd.CommandText.Replace("@passedGuid", ConvertString(passedGuid));
                    cmd.CommandText = cmd.CommandText.Replace("@item", ConvertString(s.PartNumber));
                    cmd.CommandText = cmd.CommandText.Replace("@brand", ConvertString(s.Brand));

                    cmd.ExecuteNonQuery();
                    cmd.Connection.Close();
                }
            }
        }

        private static string GetPartString(DataTable dt, int i)
        {
            var sb = new StringBuilder();
            sb.Append(dt.Rows[i]["G_ITEM"]);
            return sb.ToString();
        }

        private static string GetGuidString(DataTable dt, int i)
        {
            var sb = new StringBuilder();
            sb.Append(dt.Rows[i]["G_GUID"]);
            return sb.ToString();
        }


       

        private static void WriteToEventLog(Exception ex)
        {
            var myLog = new EventLog();
            myLog.Source = "Application Log";
            myLog.WriteEntry("as400 exception:" + ex, EventLogEntryType.Information);
        }

        private static void WriteToEventLog(String x)
        {
            var myLog = new EventLog();
            myLog.Source = "Application Log";
            myLog.WriteEntry(x, EventLogEntryType.Information);
        }

        private static string GetUnprocessMasterRecsString()
        {
            var sb = new StringBuilder();
            sb.Append(@"SELECT G_GUID, G_ITEM, G_RETRN FROM ");
            sb.Append(Settings.Default.partFileL1);
            sb.Append(" WHERE G_GUID = ");
            sb.Append('\'' + Guid + '\'');
            return sb.ToString();
        }

        private static string GetCrossPartInsertString()
        {
            var sb = new StringBuilder();
            sb.Append("INSERT into ");
            sb.Append(Settings.Default.partXRefFile);
            sb.Append(" (X_GUID,X_ITEM,X_BRAND) VALUES(@passedGuid, @item, @brand)");
            return sb.ToString();
        }

        private static string GetPartFoundUpdateMasterString()
        {
            var sb = new StringBuilder();
            sb.Append("UPDATE ");
            sb.Append(Settings.Default.partFile);
            sb.Append(
                " SET G_USRID = @usrid,G_BRANDED = @branded,G_COMCODE = @comcode,G_LEVEL = @level, G_STATUS = @status, G_RTNBLE = @rtnble, G_TARIFFCD = @tariffcd, G_AMSC = @amsc, G_TQTY = @tqty, G_SVCLIFE = @svclife, G_PKGCODE = @pkgcode, G_INFO = @info, G_RETRN = @retrn WHERE G_GUID = @passedGuid");
            return sb.ToString();
        }

        private static string GetPartNotFoundUpdateMasterString()
        {
            var sb = new StringBuilder();
            sb.Append("UPDATE ");
            sb.Append(Settings.Default.partFile);
            sb.Append(
                " SET G_RETRN = @retrn WHERE G_GUID = @passedGuid");
            return sb.ToString();
        }

        // Define the parameters for the UPDATE command in different ways
        private static void AddParameters(OleDbCommandBuilder cb)
        {
            try
            {
                cb.GetUpdateCommand().Parameters.Add("@return", OleDbType.Char, 1, "G_RETRN");
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }
}

