﻿using OHDataLogger.Classes;
using OHDataLogger.Methods;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace OHDataLogger
{
    public partial class OpenHABRest : Window
    {
        // Private
        private readonly List<string> Items = new List<string>();
        private readonly APILookup getApiData = new APILookup();
        private readonly DataSqlClasses dataSqlClasses = new DataSqlClasses();
        private CancellationTokenSource _apiTokenSource = new CancellationTokenSource();
        private CancellationTokenSource _sqlTokenSource = new CancellationTokenSource();
        private CancellationTokenSource _loginTokenSource = new CancellationTokenSource();
        private Thread TableRefresh = null;
        private Thread SqlStoreTask = null;
        private readonly Thread LogInSql = null;
        public static bool _sqlloggedIn = false;
        private bool _apiloggedIn = false;
        private bool _resetSqlInfo = true;

        // Public
        public static DateTime dtSql = new DateTime();
        public static DateTime dtApi = new DateTime();
        public static List<Items> ItemsList = new List<Items>();
        public static List<Items> ItemsListTemp = new List<Items>();
        public static string conStr;
        public static SqlConnection conn;
        public static string ApiMessages = "API Disconnected";
        public static Brush ApiColor = Brushes.Red;
        public static string SqlMessages = "SQL Disconnected";
        public static Brush SqlColor = Brushes.Red;
        public static string SqlErrMessage = "No SQL Errors";
        public static Brush SqlErrColor = Brushes.Green;
        public static string SqlTabMessage = "";
        public static Brush SqlTabColor = Brushes.Red;
        public static bool _CheckApiCon;
        public static List<Items> AddedSensor = new List<Items>();
        public bool AdSens = false;
        private readonly int sqlTimeInterval = 60;
        private readonly Secure_It secureIt = new Secure_It();

        public OpenHABRest()
        {
            InitializeComponent();
            // Check if log folder and file exists, create if not.
            if (!Directory.Exists(Directory.GetCurrentDirectory() + "/LogFile"))
            {
                _ = Directory.CreateDirectory(Directory.GetCurrentDirectory() + "/LogFile");
            }
            if (!File.Exists(Directory.GetCurrentDirectory() + "/LogFile/LogFile.txt"))
            {
                _ = File.Create(Directory.GetCurrentDirectory() + "/LogFile/LogFile.txt");
            }

            // Check app properties settings and configure as needed
            if (Properties.Settings.Default.Enabled == null)
            {
                Properties.Settings.Default.Enabled = new List<string>();
            }
            ChkEnableAll.IsChecked = Properties.Settings.Default.EnableAll;
            tbUpdateSpeed.Text = Properties.Settings.Default.UpdateInterval.ToString();
            // Update the window
            UpdateGui(true, true, true);
            Title = "OpenHAB DataLogger";
            if (Properties.Settings.Default.RememberApiLogin)
            {
                tbApiIp.Text = Properties.Settings.Default.ApiAddr;
                ChkRememberApi.IsChecked = true;
                //if (Properties.Settings.Default.AutoLogon == true)
                BtnConnectApi_Click(null, null);
            }
            if (Properties.Settings.Default.RememberSqlLogin)
            {
                userSql.Text = secureIt.DecryptString(Properties.Settings.Default.UserSql);
                passSql.Password = secureIt.DecryptString(Properties.Settings.Default.PassSql);
                tbSqlIp.Text = Properties.Settings.Default.SqlIpAddr;
                if (Properties.Settings.Default.SqlPort != "")
                    tbSqlIp.Text += ":" + Properties.Settings.Default.SqlPort;
                tbDatabaseName.Text = Properties.Settings.Default.SqlDbName;
                ChkRememberSql.IsChecked = true;
                //if(Properties.Settings.Default.AutoLogon == true) 
                BtnLogInSql_Click(null, null);
            }
        }

        // Update GUI messaged and colors
        void UpdateGui(bool _Sql, bool _Api = false, bool _Err = false)
        {
            if (_Sql)
            {
                statSqlCon.Text = SqlMessages;
                statSqlConItem.Background = SqlColor;
            }
            if (_Api)
            {
                statApiCon.Text = ApiMessages;
                statApiConItem.Background = ApiColor;
            }
            if (_Err)
            {
                statSqlErr.Text = SqlErrMessage;
                statSqlErrItem.Background = SqlErrColor;
            }
        }

        // Check and validate login details
        void UpdateSqlUserPass(string _LoggedIn)
        {
            switch (_LoggedIn)
            {
                case "UserAccepted":
                    userSql.IsEnabled = false;
                    passSql.IsEnabled = false;
                    tbSqlIp.IsEnabled = false;
                    tbDatabaseName.IsEnabled = false;
                    userSql.Background = Brushes.Green;
                    passSql.Background = Brushes.Green;
                    tbSqlIp.Background = Brushes.Green;
                    tbDatabaseName.Background = Brushes.Green;
                    break;
                case "UserNotAccepted":
                    Dispatcher.Invoke(() =>
                    {
                        tbSqlIp.Background = !Functions.CheckValidIp(tbSqlIp.Text) ? Brushes.Red : Brushes.Orange;
                        tbDatabaseName.Background = tbDatabaseName.Text != "" ? Brushes.Orange : Brushes.Red;
                        userSql.Background = userSql.Text != "" ? Brushes.Orange : Brushes.Red;
                        passSql.Background = passSql.Password != "" ? Brushes.Orange : Brushes.Red;
                        statSqlErr.Text = "Sql Login Error";
                        statSqlErrItem.Background = Brushes.Red;
                    });
                    break;
                case "Wrong IP":
                    tbSqlIp.Background = Brushes.Red;
                    break;
                case "LogOut":
                    userSql.IsEnabled = true;
                    passSql.IsEnabled = true;
                    tbSqlIp.IsEnabled = true;
                    tbDatabaseName.IsEnabled = true;
                    if (!Properties.Settings.Default.RememberSqlLogin)
                    {
                        userSql.Text = "";
                        passSql.Password = "";
                    }
                    userSql.Background = Brushes.White;
                    passSql.Background = Brushes.White;
                    tbSqlIp.Background = Brushes.White;
                    tbDatabaseName.Background = Brushes.White;
                    break;
            }
        }

        // Start SQL task, create tables if missing
        void RunSql()
        {
            foreach (Items item in ItemsList)
            {
                if (!dataSqlClasses.Tables.Contains(item.name))
                {
                    dataSqlClasses.CreateTables(item.name, item.type);
                }
            }
            if (SqlTabMessage != "")
            {
                statSqlTab.Text = SqlTabMessage;
                statSqlTabItem.Visibility = Visibility.Visible;
            }
            // Configure SQL Storage task and start same
            SqlStoreTask = new Thread(StoreSqlCall)
            {
                IsBackground = true
            };
            SqlStoreTask.Start();
        }

        // Start API connection
        void RunApi()
        {
            Functions.SaveApiDetails(tbApiIp.Text, ChkRememberSql.IsChecked);
            getApiData.RestConn();
            getApiData.ItemsDict.Clear();
            Items.Clear();
            API_UpdateDict();
            getApiData.PopulateDataTable();
            dgSensors.DataContext = getApiData.ItemsTable.AsDataView();
            TableRefresh = new Thread(ApiUpdate);
            TableRefresh.Start();
            // Start SQL Storing task if certain settings is OK and its not running.
            if (SqlStoreTask == null && btnSqlLogin.Content.ToString() != "Connect")
            {
                SqlStoreTask = new Thread(StoreSqlCall)
                {
                    IsBackground = true
                };
                SqlStoreTask.Start();
            }
        }

        // Update API data.
        private void ApiUpdate()
        {
            Stopwatch stopW = new Stopwatch();
            while (!_apiTokenSource.Token.IsCancellationRequested)
            {
                if (DateTime.Now.Second % NumValue == 0)
                {
                    try
                    {
                        stopW.Restart();
                        dtApi = DateTime.Now;
                        Items.Clear();
                        if (getApiData.ItemsDict.Count == 0)
                        {
                            API_UpdateDict();
                            getApiData.PopulateDataTable();
                            dgSensors.DataContext = getApiData.ItemsTable.AsDataView();
                        }
                        else
                        {
                            if (AddedSensor.Count != 0)
                            {
                                getApiData.UpdateDataTable(AddedSensor);
                                AddedSensor.Clear();
                                AdSens = true;
                            }
                            API_UpdateDict(true);
                        }
                        Dispatcher.Invoke(() =>
                        {
                            if (AdSens)
                            {
                                dgSensors.DataContext = getApiData.ItemsTable.AsDataView();
                                AdSens = false;
                            }
                            if (dgSensors.IsKeyboardFocusWithin)
                            {
                                dgSensors.Items.Refresh();
                                _ = dgSensors.Focus();
                            }
                            else
                            {
                                dgSensors.Items.Refresh();
                            }

                            UpdateGui(false, true, false);
                        });
                        stopW.Stop();
                        bool _apiCancellationTriggered = _apiTokenSource.Token.WaitHandle.WaitOne((NumValue * 1000) - (int)stopW.Elapsed.TotalMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogMessage(ex.Message, ErrorLevel.API);
                    }
                }
            }
        }

        private void API_UpdateDict(bool update = false)
        {
            if (!update)
            {
                getApiData.PopulateItemsDict();
            }
            else
            {
                getApiData.UpdateItemsDict();
            }
        }

        // Main SQL storing loop.
        private void StoreSqlCall()
        {
            int min = DateTime.Now.Minute;
            int hour = DateTime.Now.Hour;

            try
            {
                bool watcher = false;
                bool firstrun = true;
                Stopwatch stopW = new Stopwatch();
                while (!_sqlTokenSource.Token.IsCancellationRequested)
                {
                    if (watcher)
                    {
                        if (!firstrun)
                        {
                            _ = _sqlTokenSource.Token.WaitHandle.WaitOne((sqlTimeInterval*1000) - (int)stopW.Elapsed.TotalMilliseconds);
                        }

                        stopW.Restart();
                        dtSql = DateTime.Now;
                        if (dtSql.Second % sqlTimeInterval != 0 && !firstrun)
                        {
                            watcher = false;
                        }

                        firstrun = false;
                        try
                        {
                            if (ItemsListTemp.Count > 0 && !_resetSqlInfo)
                            {
                                dataSqlClasses.StoreValuesToSql();
                                statSqlCon.Dispatcher.Invoke(() =>
                                {
                                    UpdateGui(true, false, true);
                                    statSqlLastStore.Text = dtSql.ToString();
                                });
                            }
                            else
                            {
                                if (!_apiloggedIn)
                                {
                                    SqlMessages = "SQL Connected";
                                    SqlColor = Brushes.Green;
                                    SqlErrMessage = "No API Data";
                                    SqlErrColor = Brushes.Orange;
                                    _resetSqlInfo = true;
                                }
                                else if (_resetSqlInfo)
                                {
                                    SqlMessages = "SQL Connected and storing";
                                    SqlColor = Brushes.Green;
                                    _resetSqlInfo = false;
                                    dataSqlClasses.StoreValuesToSql();
                                    statSqlCon.Dispatcher.Invoke(() =>
                                    {
                                        UpdateGui(true, false, true);
                                        statSqlLastStore.Text = dtSql.ToString();
                                    });
                                }
                                statSqlCon.Dispatcher.Invoke(() =>
                                {
                                    UpdateGui(true, false, true);
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogMessage(ex.Message, ErrorLevel.SQL);
                        }
                        stopW.Stop();
                    }
                    else if (DateTime.Now.Second % sqlTimeInterval == 0 && !watcher)
                    {
                        watcher = true;
                        firstrun = true;
                    }
                    // Sleep for the amount 
                    else if (DateTime.Now.Second % sqlTimeInterval < (sqlTimeInterval - 1 ) && !watcher)
                    {
                        _ = _sqlTokenSource.Token.WaitHandle.WaitOne((sqlTimeInterval * 1000) - (DateTime.Now.Second % sqlTimeInterval * 1000));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogMessage(ex.Message, ErrorLevel.THREAD);
            }
        }

        private void LogInThread(string _Ip)
        {
            try
            {
                if (!Functions.CheckValidIp(_Ip))
                {
                    throw new Exception("Wrong IP");
                }
                conStr = ConSQL.GetConnectionString_up();
                conn = new SqlConnection(conStr);
                conn.Open();
                conn.Close();
                Dispatcher.Invoke(() =>
                {
                    UpdateSqlUserPass("UserAccepted");
                    SqlMessages = "SQL Connected";
                    SqlColor = Brushes.Green;
                    UpdateGui(true, false, true);
                    btnSqlLogin.Content = "Disconnect";
                });
                _sqlloggedIn = true;
                dataSqlClasses.GetSqlTables();
                Dispatcher.Invoke(() =>
                {
                    RunSql();
                });
                //Task.Run(RunSql);
            }
            catch (Exception ex)
            {
                if (ex.Message == "Wrong IP")
                {
                    UpdateSqlUserPass(ex.Message);
                }
                else
                {
                    UpdateSqlUserPass("UserNotAccepted");
                }

                Logger.LogMessage(ex.Message, ErrorLevel.LOGIN);
            }
            finally
            {
                if (conn != null)
                {
                    conn.Close();
                }
                Dispatcher.Invoke(() =>
                {
                    btnSqlLogin.IsEnabled = true;
                    if (btnSqlLogin.Content.ToString() == "Connecting...")
                    {
                        btnSqlLogin.Content = "Connect";
                    }
                });
            }
        }

        private void ConnectApi(bool LogIn = true)
        {
            if (LogIn && Functions.CheckValidIp(tbApiIp.Text, true))
            {
                _apiTokenSource = new CancellationTokenSource();
                Functions.SaveApiDetails(tbApiIp.Text, ChkRememberApi.IsChecked);
                getApiData.RestConn(true);
                if (_CheckApiCon)
                {
                    RunApi();
                    if (getApiData.ItemsDict.Count > 0)
                    {
                        btnConnectApi.Content = "Disconnect";
                        tbApiIp.IsEnabled = false;
                        SqlErrMessage = "Waiting for API data";
                        SqlErrColor = Brushes.LightGreen;
                        if (tbApiIp.Background == Brushes.White || tbApiIp.Background == Brushes.Red)
                        {
                            tbApiIp.Background = Brushes.Green;
                        }
                        UpdateGui(false, true, true);
                        _apiloggedIn = true;
                    }
                    return;
                }
                if (tbApiIp.Background == Brushes.Green || tbApiIp.Background == Brushes.White)
                {
                    tbApiIp.Background = Brushes.Red;
                }
                tbApiIp.IsEnabled = true;
                return;
            }
            if (_apiloggedIn)
            {
                _apiTokenSource.Cancel();
                TableRefresh.Interrupt();
                //if (TableRefresh != null)
                //    TableRefresh.Abort();
                getApiData.ItemsTable.Clear();
                ItemsList.Clear();
                ItemsListTemp.Clear();
                btnConnectApi.IsEnabled = true;
                tbApiIp.IsEnabled = true;
                btnConnectApi.Content = "Connect";
                ApiMessages = "API Disconnected";
                SqlErrMessage = "API Lost";
                SqlErrColor = Brushes.PaleVioletRed;
                ApiColor = Brushes.Red;
                if (tbApiIp.Background == Brushes.Red || tbApiIp.Background == Brushes.Green)
                {
                    tbApiIp.Background = Brushes.White;
                }
                UpdateGui(false, true, true);
                _apiloggedIn = false;
                return;
            }
            tbApiIp.Background = Brushes.Red;
        }

        private async Task LogInOutSql(bool LogIn = true) //async void LogInOutSql(bool LogIn = true)
        {
            if (LogIn)
            {
                if (Functions.CheckValidIp(tbSqlIp.Text) && tbDatabaseName.Text != "" && userSql.Text != "" && passSql.Password != "")
                {
                    _sqlTokenSource = new CancellationTokenSource();
                    _loginTokenSource = new CancellationTokenSource();
                    btnSqlLogin.IsEnabled = false;
                    btnSqlLogin.Content = "Connecting...";
                    string _Ip = tbSqlIp.Text;
                    Functions.SaveSqlUser(_Ip, tbDatabaseName.Text, secureIt.EncryptString(userSql.Text), secureIt.EncryptString(passSql.Password), ChkRememberSql.IsChecked);
                    //LogInSql = new Thread(() => LogInThread(_Ip));
                    //LogInSql.Start();
                    await Task.Run(() => LogInThread(_Ip), _loginTokenSource.Token);
                }
                else
                {
                    UpdateSqlUserPass("UserNotAccepted");
                }
            }
            else
            {
                if (_sqlloggedIn)
                {
                    if (SqlStoreTask != null)
                    {
                        _sqlTokenSource.Cancel();
                        //SqlStoreTask.Interrupt();
                        //SqlStoreTask.Abort();
                    }
                    _loginTokenSource.Cancel();
                    //if (SqlStoreTask != null)
                    //    SqlStoreTask.Abort();
                    //if (LogInSql != null)
                    //    LogInSql.Abort();
                    UpdateSqlUserPass("LogOut");
                    SqlMessages = "SQL Disconnected";
                    SqlColor = Brushes.Red;
                    UpdateGui(true, true, false);
                    btnSqlLogin.Content = "Connect";
                    _sqlloggedIn = false;
                }
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            //_apiTokenSource.Cancel();
            //_sqlTokenSource.Cancel();
            //_loginTokenSource.Cancel();

            if (TableRefresh != null)
            {
                _apiTokenSource.Cancel();
                TableRefresh.Interrupt();
                //TableRefresh.Abort();
            }
            if (SqlStoreTask != null)
            {
                _sqlTokenSource.Cancel();
                SqlStoreTask.Interrupt();
                //SqlStoreTask.Abort();
            }
            if (LogInSql != null)
            {
                _loginTokenSource.Cancel();
                //LogInSql.Abort();
            }
            if (_sqlloggedIn)
            {
                if (ChkRememberSql.IsChecked == false)
                {
                    Properties.Settings.Default.UserSql = "";
                    Properties.Settings.Default.PassSql = "";
                    Properties.Settings.Default.SqlIpAddr = "";
                    Properties.Settings.Default.SqlPort = "";
                    Properties.Settings.Default.SqlDbName = "";
                    Properties.Settings.Default.Save();
                }
                if (ChkRememberApi.IsChecked == false)
                {
                    Properties.Settings.Default.ApiAddr = "";
                    Properties.Settings.Default.Save();
                }
                conn.Close();
            }
            Application.Current.Shutdown();
        }

        private void TbUpdateSpeed_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Functions.IsTextAllowed(e.Text, @"[^0-9]");
        }

        private void PassSql_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                btnSqlLogin.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
        }

        private async void BtnLogInSql_Click(object sender, RoutedEventArgs e)
        {
            if (btnSqlLogin.Content.ToString() == "Connect")
            {
                await LogInOutSql();
            }
            else
            {
                await LogInOutSql(false);
            }
        }

        private void BtnConnectApi_Click(object sender, RoutedEventArgs e)
        {
            if (btnConnectApi.Content.ToString() == "Connect")
            {
                ConnectApi();
            }
            else
            {
                ConnectApi(false);
            }
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            TextBlock content = (TextBlock)dgSensors.SelectedCells[0].Column.GetCellContent(dgSensors.SelectedCells[0].Item);
            _ = Process.Start("http://" + Properties.Settings.Default.ApiAddr + "/rest/items/" + content.Text);
        }

        private void ChkRememberSql_Checked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.RememberSqlLogin = true;
            Properties.Settings.Default.Save();

        }

        private void ChkRememberSql_Unchecked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.RememberSqlLogin = false;
            Properties.Settings.Default.Save();
        }

        private void TbSqlIp_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Functions.IsTextAllowed(e.Text, @"[^0-9:.]");
        }

        #region NumericUpDown
        private int _numValue = 0;

        public int NumValue
        {
            get => _numValue;
            set
            {
                _numValue = value;
                tbUpdateSpeed.Text = value.ToString();
                Functions.SaveUpdateInterval(tbUpdateSpeed.Text);
            }
        }

        private void CmdUp_Click(object sender, RoutedEventArgs e)
        {
            if (NumValue < 10)
            {
                NumValue++;
            }
        }

        private void CmdDown_Click(object sender, RoutedEventArgs e)
        {
            if (NumValue > 1)
            {
                NumValue--;
            }
        }

        private void TbUpdateSpeed_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (tbUpdateSpeed == null)
            {
                return;
            }

            if (!int.TryParse(tbUpdateSpeed.Text, out _numValue))
            {
                tbUpdateSpeed.Text = _numValue.ToString();
            }
        }
        #endregion

        private void ChkRememberApi_Checked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.RememberApiLogin = true;
            Properties.Settings.Default.Save();
        }

        private void ChkRememberApi_Unchecked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.RememberApiLogin = false;
            Properties.Settings.Default.Save();
        }

        private void OnChecked(object sender, RoutedEventArgs e)
        {
            DataGridRow RowData = (DataGridRow)dgSensors.ItemContainerGenerator.ContainerFromIndex(dgSensors.SelectedIndex);
            DataGridCell RowColumn = dgSensors.Columns[0].GetCellContent(RowData).Parent as DataGridCell;
            string itemName = ((TextBlock)RowColumn.Content).Text;
            getApiData.EnableItems((bool)(sender as CheckBox).IsChecked, itemName);
        }

        private void CheckUnCheckAll(object sender, RoutedEventArgs e)
        {
            DataGridCheckBoxColumn firstCol = dgSensors.Columns.OfType<DataGridCheckBoxColumn>().FirstOrDefault(c => c.DisplayIndex == 4);

            getApiData.EnableItems((bool)(sender as CheckBox).IsChecked);
            Properties.Settings.Default.EnableAll = (bool)(sender as CheckBox).IsChecked;
            Properties.Settings.Default.Save();
        }
    }
}