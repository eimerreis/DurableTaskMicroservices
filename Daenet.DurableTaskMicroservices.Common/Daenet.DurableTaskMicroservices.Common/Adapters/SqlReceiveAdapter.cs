﻿using Daenet.DurableTaskMicroservices.Common;
using Daenet.DurableTaskMicroservices.Common.BaseClasses;
using Daenet.DurableTaskMicroservices.Common.Entities;
using Daenet.DurableTaskMicroservices.Common.Logging;
using DurableTask;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Daenet.System.Integration
{
    public class SqlReceiveAdapter<TAdapterOutput, TAdapterInput> : ReceiveAdapterBase<TAdapterInput, TAdapterOutput> 
        where TAdapterOutput : class
        where TAdapterInput : TaskInput
    {
        protected override TAdapterOutput ReceiveData(TaskContext context, TAdapterInput taskInput)
        {
            SqlReceiveAdapterConfig config = this.GetConfiguration<SqlReceiveAdapterConfig>(taskInput.Orchestration);

            TAdapterOutput res = null;

            base.LogManager.TraceInfMethodStarted("Daenet.System.Integration.SqlReceiveAdapter.RunTask()");
            
            if (String.IsNullOrEmpty(config.ConnectionString))
                throw new Exception("SqlReceiveAdapter must have valid ConnectionString. Please check your configuration.");
            if (String.IsNullOrEmpty(config.CheckDataCmdText))
                throw new Exception("SqlReceiveAdapter must have valid CheckDataCmdText. Please check your configuration.");
            if (String.IsNullOrEmpty(config.FetchDataCmdText))
                throw new Exception("SqlReceiveAdapter must have valid FetchDataCmdText. Please check your configuration.");
            if (String.IsNullOrEmpty(config.MapperQualifiedName))
                throw new Exception("SqlReceiveAdapter must have valid MapperQualifiedName. Please check your configuration.");

            using (SqlConnection connection = new SqlConnection(config.ConnectionString))
            {
                connection.Open();

                SqlCommand checkCmd = connection.CreateCommand();


                // Start a local transaction.
                SqlTransaction transaction = connection.BeginTransaction("SqlReceiveAdapterTransaction");

                // Must assign both transaction object and connection to Command object for a pending local transaction
                checkCmd.Connection = connection;
                checkCmd.Transaction = transaction;
                checkCmd.CommandText = config.CheckDataCmdText;

                try
                {
                    var nrOfRecords = checkCmd.ExecuteScalar();

                    if (!(nrOfRecords is int))
                    {
                        throw new Exception("The SqlReceiveAdapter SQL command defined with CheckDataCmdText must return numeric value greater or equal zero. Only if the returned value is greater zero, then the command defined by FetchDataCmdText is going to be executed.");
                    }
                    else
                    {
                        base.LogManager.TraceInfCheckDataCmdTextResults(checkCmd.CommandText, nrOfRecords.ToString());

                        // Only if the CheckDataCmdText statement delivered value greater 0, then we should execute the FetchDataCmdText command to get data.
                        if ((nrOfRecords as int?).HasValue && (nrOfRecords as int?).Value > 0)
                        {

                            SqlCommand fetchCmd = connection.CreateCommand();
                            fetchCmd.Connection = connection;
                            fetchCmd.Transaction = transaction;
                            fetchCmd.CommandText = config.FetchDataCmdText;

                            SqlDataReader reader = fetchCmd.ExecuteReader();

                            base.LogManager.TraceInfFetchDataCmdTextResults(fetchCmd.CommandText, reader.RecordsAffected.ToString());

                            var mapper = Factory.GetAdapterMapper(config.MapperQualifiedName);

                            res = (TAdapterOutput)mapper.Map(reader);
                        }

                    }

                    // Attempt to commit the transaction.
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    // Attempt to roll back the transaction. 
                    try
                    {
                        base.LogManager.TraceErryAdapterExecution(ex, "Daenet.System.Integration.ReceiveSqlAdapter");
                        transaction.Rollback();
                        base.LogManager.TraceInfTransactionRolledBack();
                        throw;
                    }
                    catch (Exception ex2)
                    {
                        // This catch block will handle any errors that may have occurred 
                        // on the server that would cause the rollback to fail, such as 
                        // a closed connection.
                        base.LogManager.TraceErrFailedToCommitTransactionRollback(ex2, "Daenet.System.Integration.SqlReceiveAdapter");
                        throw;
                        
                    }
                }
            }

            base.LogManager.TraceInfMethodCompleted("Daenet.System.Integration.SqlReceiveAdapter.RunTask()");

            return res;
        }
    }
}
