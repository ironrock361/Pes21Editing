using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Timers;
using Pes21Editing.IO;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using Pes2021Api;
using SofaScoreApi;
using System.ComponentModel;
using System.Windows.Controls.Primitives;
using Newtonsoft.Json;
using Pes2021Api.decrypter;
using System.Collections.ObjectModel;
using System.Data;
using GongSolutions.Wpf.DragDrop;
using System.Windows.Threading;

namespace Pes21Editing
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IDropTarget
    {
        public Dictionary<uint, Competition> Competitions = new Dictionary<uint, Competition>();
        public List<TeamPlayerLink> TeamPlayerLink = new List<TeamPlayerLink>();
        public ObservableCollection<Player> TeamPlayersView {get;set;}
        public string EditFilePath;
        private FileInfo OriginalFileInfo;
        private byte[] OriginalData;

        private string PlayerAssignmentBinPath;
        private string TacticsBinPath;
        private string TacticFormationsBinPath;
        private string PlayerBinPath;
        private string TeamBinPath;
        private int rowIndex;

        private BackgroundWorker bwPlayerSearch;
        private static object PesPlayersLock = new object();
        private static object PesTeamsLock = new object();
        private static object PesPlayerAssignmentsLock = new object();
        private static object PesTacticsLock = new object();
        private static object PesTacticFormationsLock = new object();
        private static object SofaTeamsLock = new object();
        private BackgroundWorker bwPesPlayerLoad;
        private BackgroundWorker bwPesTeamsLoad;
        private BackgroundWorker bwPesTacticsLoad;
        private BackgroundWorker bwPesTacticFormationsLoad;
        private BackgroundWorker bwPesPlayerAssignmentsLoad;
        private DispatcherTimer messageTimer = new DispatcherTimer();

        public MainWindow()
        {
            TeamPlayersView = new ObservableCollection<Player>();
            DataContext = this;
            InitializeComponent();

            BindingOperations.EnableCollectionSynchronization(Players.List, PesPlayersLock);
            BindingOperations.EnableCollectionSynchronization(Teams.List, PesTeamsLock);
            BindingOperations.EnableCollectionSynchronization(TeamPlayerLinks.List, PesPlayerAssignmentsLock);
            BindingOperations.EnableCollectionSynchronization(Tactics.List, PesTacticsLock);
            BindingOperations.EnableCollectionSynchronization(TacticFormations.List, PesTacticFormationsLock);
            BindingOperations.EnableCollectionSynchronization(SofaApi.Teams, SofaTeamsLock);

            Players.BinaryReadCompleted += Players_BinaryReadCompleted;
            Teams.BinaryReadCompleted += Teams_BinaryReadCompleted;
            TeamPlayerLinks.BinaryReadCompleted += TeamPlayerLinks_BinaryReadCompleted;
            Tactics.BinaryReadCompleted += Tactics_BinaryReadCompleted;
            TacticFormations.BinaryReadCompleted += TacticFormations_BinaryReadCompleted;

            ChooseDirectory();

            // background workers

            prgStatus.Dispatcher.Invoke(() =>
            {
                prgStatus.IsIndeterminate = true;
            });

            // load teams
            bwPesTeamsLoad = new BackgroundWorker();
            bwPesTeamsLoad.DoWork += (object s, DoWorkEventArgs dwea) =>
            {
                lock (PesTeamsLock)
                {
                    ReadTeamBin(TeamBinPath);
                }
            };

            // load players
            bwPesPlayerLoad = new BackgroundWorker();
            bwPesPlayerLoad.DoWork += (object s, DoWorkEventArgs dwea) =>
            {
                lock (PesPlayersLock)
                {
                    ReadPlayerBin(PlayerBinPath);
                }
            };

            // load player assignments
            bwPesPlayerAssignmentsLoad = new BackgroundWorker();
            bwPesPlayerAssignmentsLoad.DoWork += (object s, DoWorkEventArgs dwea) =>
            {
                lock (PesPlayerAssignmentsLock)
                {
                    ReadPlayerAssignmentBin(PlayerAssignmentBinPath);
                }
            };

            // load tactics 
            bwPesTacticsLoad = new BackgroundWorker();
            bwPesTacticsLoad.DoWork += (object s, DoWorkEventArgs dwea) =>
            {
                lock (PesTacticsLock)
                {
                    ReadTacticsBin(TacticsBinPath);
                }
            };

            // load tactic formations 
            bwPesTacticFormationsLoad = new BackgroundWorker();
            bwPesTacticFormationsLoad.DoWork += (object s, DoWorkEventArgs dwea) =>
            {
                lock (PesTacticFormationsLock)
                {
                    ReadTacticFormationsBin(TacticFormationsBinPath);
                }
            };

            // pes player search
            bwPlayerSearch = new BackgroundWorker();
            bwPlayerSearch.DoWork += BwPlayerSearch_DoWork;
            bwPlayerSearch.RunWorkerCompleted += BwPlayerSearch_RunWorkerCompleted;
            LoadFilesAsync();
        }

        private void LoadFilesAsync()
        {
            bwPesTeamsLoad.RunWorkerAsync();
            bwPesPlayerLoad.RunWorkerAsync();
            bwPesPlayerAssignmentsLoad.RunWorkerAsync();
            bwPesTacticsLoad.RunWorkerAsync();
            bwPesTacticFormationsLoad.RunWorkerAsync();
        }

        private void BwPlayerSearch_DoWork(object sender, DoWorkEventArgs e)
        {
            string text = txtAllPesPlayerSearch.Dispatcher.Invoke(()=> txtAllPesPlayerSearch.Text.ToLowerInvariant());
            List<Player> players=Players.List.ToList();
            
            if (!string.IsNullOrEmpty(text))
            {
                players = players.Where(player =>
                                 player.ID.ToString().Contains(text) ||
                                 player.Name.ToLowerInvariant().Contains(text) ||
                                 player.PrintNameClub.ToLowerInvariant().Contains(text) ||
                                 player.PrintNameNationalTeam.ToLowerInvariant().Contains(text) ||
                                 Countries.Get(player.NationalityID).ToLowerInvariant().Contains(text)).ToList();
            }
            e.Result = players;
        }

        private void BwPlayerSearch_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            lstAllPesPlayers.Dispatcher.Invoke(() =>
            {
                lstAllPesPlayers.ItemsSource = e.Result as List<Player>;

                prgStatus.Dispatcher.Invoke(() =>
                {
                    prgStatus.IsIndeterminate = false;
                });
            });
        }

        public ICollectionView TeamsView
        {
            get
            {
                var collectionView = CollectionViewSource.GetDefaultView(Teams.List);
                var txt = txtPesTeamSearch.Dispatcher.Invoke(() => txtPesTeamSearch.Text);

                collectionView.Filter = item =>
                {
                    Team team = item as Team;
                    return team != null &&
                        (team.TeamName.ToLowerInvariant().Contains(txt.ToLowerInvariant()) ||
                         Countries.Get(team.TeamNationality).ToLowerInvariant().Contains(txt.ToLowerInvariant()));
                };

                return collectionView;
            }
        }

        public ICollectionView ClubTeams
        {
            get
            {
                var collectionView = new CollectionViewSource { Source = Teams.List }.View;

                collectionView.Filter = item =>
                {
                    Team team = item as Team;
                    return team != null && !team.IsNationalTeam;
                };

                return collectionView;
            }
        }
        public void TeamPlayersViewRefresh()
        {
            var team = lstPesTeams.SelectedItem as Team;
            TeamPlayersView.Clear();

            if (team != null)
            {
                var players = team.Players.Where(item =>
                {
                    Player player = item as Player;

                    bool ret = true;

                    if (!string.IsNullOrEmpty(txtPesPlayerSearch.Text))
                    {
                        ret &= player != null &&
                            (player.Name.ToLowerInvariant().Contains(txtPesPlayerSearch.Text.ToLowerInvariant()) ||
                             player.PrintNameClub.ToLowerInvariant().Contains(txtPesPlayerSearch.Text.ToLowerInvariant()));// ||
                                                                                                                           //player.Nationality.ToLowerInvariant().Contains(txtPesPlayerSearch.Text.ToLowerInvariant()));
                    }

                    return ret;
                }).ToList();

                players.ForEach(p => TeamPlayersView.Add(p));
            }
        }
        public ICollectionView SofaTeamsView
        {
            get
            {
                var collectionView = CollectionViewSource.GetDefaultView(SofaApi.Teams);
                var tag = (cmbSofaLeague.SelectedItem as ComboBoxItem)?.Tag;
                int tournamentId = SofaApi.ConvertToInt32(tag);

                collectionView.Filter = item => {
                    SofaTeam team = item as SofaTeam;
                    return team != null && team.Tournaments.Contains(tournamentId) &&
                        (team.TeamName.ToLowerInvariant().Contains(txtSofaTeamSearch.Text.ToLowerInvariant()) ||
                         team.TeamNationality.ToLowerInvariant().Contains(txtSofaTeamSearch.Text.ToLowerInvariant()));
                };

                return collectionView;
            }
        }

        public ICollectionView SofaPlayersView
        {
            get
            {
                var team = lstSofaTeams.SelectedItem as SofaTeam;

                if (team != null)
                {
                    var collectionView = CollectionViewSource.GetDefaultView(team.Players);

                    collectionView.Filter = item =>
                    {
                        SofaPlayer player = item as SofaPlayer;
                        //bool ret = ret = player.TeamID == (lstSofaTeams.SelectedItem as SofaTeam)?.ID;
                        bool ret = true;

                        if (!string.IsNullOrEmpty(txtSofaPlayerSearch.Text))
                        {
                            ret &= player != null &&
                                (player.PlayerName.ToLowerInvariant().Contains(txtSofaPlayerSearch.Text.ToLowerInvariant()) ||
                                 player.PrintNameClub.ToLowerInvariant().Contains(txtSofaPlayerSearch.Text.ToLowerInvariant()) ||
                                 player.Nationality.ToLowerInvariant().Contains(txtSofaPlayerSearch.Text.ToLowerInvariant()));
                        }


                        return ret;
                    };

                    return collectionView;
                }
                return CollectionViewSource.GetDefaultView(new List<SofaPlayer>());
            }
        }

        private string PesifyString(string name)
        {
            var splits = name.Split(new[] { '.', ' ' });

            if (splits.Length > 1)
            {
                return $"{splits[0][0]}. {splits.Last()}";
            }

            return splits.Last();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            ChooseDirectory();
            LoadFiles();
        }

        private void ChooseDirectory()
        {
            var lastFile = Properties.Settings.Default.LastDir;

            var dlg = new CommonOpenFileDialog()
            {
                InitialDirectory = lastFile,
                IsFolderPicker = true
            };

            if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
            {
                Properties.Settings.Default.LastDir = dlg.FileName;
                Properties.Settings.Default.Save();

                PlayerBinPath = Path.Combine(dlg.FileName, "Player.bin");
                TeamBinPath = Path.Combine(dlg.FileName, "Team.bin");
                PlayerAssignmentBinPath = Path.Combine(dlg.FileName, "PlayerAssignment.bin");
                TacticsBinPath = Path.Combine(dlg.FileName, "Tactics.bin");
                TacticFormationsBinPath = Path.Combine(dlg.FileName, "TacticsFormation.bin");

            }
        }

        private void LoadFiles()
        {
            ReadTeamBin(TeamBinPath);
            ReadPlayerBin(PlayerBinPath);
            ReadPlayerAssignmentBin(PlayerAssignmentBinPath);
            ReadTacticsBin(TacticsBinPath);
            ReadTacticFormationsBin(TacticFormationsBinPath);
        }

        private void UnZlibDirectory(string dirName)
        {
            foreach (var filename in Directory.GetFiles(dirName))
            {
                if (!Path.GetFileName(filename).StartsWith("unzlib_") && filename.EndsWith(".bin"))
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (FileStream file = new FileStream(filename, FileMode.Open, FileAccess.Read))
                        {
                            file.CopyTo(ms);
                            var bytes = BinFile.UnZlib(ms.ToArray());

                            var dir = Path.GetDirectoryName(filename);
                            var fname = Path.GetFileNameWithoutExtension(filename);
                            var ext = Path.GetExtension(filename);

                            var unzlibPath = Path.Combine(dir, $"unzlib_{fname}_{DateTime.Now:d.M.y-H.mm}{ext}");

                            using (FileStream fsw = new FileStream(unzlibPath, FileMode.OpenOrCreate, FileAccess.Write))
                            {
                                fsw.Write(bytes, 0, bytes.Length);
                            }
                        }
                    }
                }
            }
            ShowTimedMessage($"All files unzlibbed in directory: {dirName}");
        }

        public void ReadPlayerBin(string filename)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (FileStream file = new FileStream(filename, FileMode.Open, FileAccess.Read))
                {
                    file.CopyTo(ms);

                    Players.Data = ms.ToArray();
                }
            }

        }

        private void Players_BinaryReadCompleted(object sender, EventArgs e)
        {
            Console.WriteLine($"Num of players: {Players.List.Count}");

            //foreach(var p in Players.List.OrderByDescending(p => p.Rating(Position.LB)).Take(10))
            //{
            //    Console.WriteLine($"{p.Name}: {p.Rating(Position.LB)}");
            //}

            QueuedPlayerSearch();
        }

        public void ReadTeamBin(string filename)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (FileStream file = new FileStream(filename, FileMode.Open, FileAccess.Read))
                {
                    file.CopyTo(ms);

                    Teams.Data = ms.ToArray();
                }
            }

        }

        private void Teams_BinaryReadCompleted(object sender, EventArgs e)
        {
            Console.WriteLine($"Num of teams: {Teams.List.Count}");

            TeamsView.Refresh();
            ClubTeams.Refresh();
        }

        public int ReadPlayerAssignmentBin(string filename)
        {
            int i = 0;

            using (MemoryStream ms = new MemoryStream())
            {
                using (FileStream file = new FileStream(filename, FileMode.Open, FileAccess.Read))
                {
                    file.CopyTo(ms);

                    TeamPlayerLinks.Data = ms.ToArray();

                    //var bytes = BinFile.UnZlib(ms.ToArray());

                    //int offset = 0;

                    //while (bytes.Length - offset >= 16)
                    //{
                    //    var playerAssignmentData = new ArraySegment<byte>(bytes, offset, 16).ToArray();
                    //    var tpl = new TeamPlayerLink(playerAssignmentData);
                    //    offset += 16;

                    //    var player = Players[tpl.PlayerID];

                    //    if (Teams.TryGetValue(tpl.TeamID, out Team team))
                    //    {
                    //        if (team.IsNationalTeam)
                    //        {
                    //            player.NationalTeam = team;
                    //        }
                    //        else
                    //        {
                    //            player.Team = team;
                    //        }

                    //        team.Players.Add(player);
                    //    }
                    //    else
                    //    {
                    //        Console.WriteLine($"{player.ID};{player.PlayerName} takim bulunamadı: {tpl.TeamID}");
                    //    }
                    //}
                }
            }

            return i;
        }

        private void TeamPlayerLinks_BinaryReadCompleted(object sender, EventArgs e)
        {
            Console.WriteLine($"Num of teams-player links: {TeamPlayerLinks.List.Count}");

            foreach (var tpl in TeamPlayerLinks.List)
            {
                var player = Players.GetPlayer(tpl.PlayerID);
                var team = Teams.GetTeam(tpl.TeamID);

                if (player != null && team != null)
                {
                    if (team.IsNationalTeam)
                    {
                        player.ShirtNoNational = tpl.ShirtNo;
                    }
                    else
                    {
                        player.ShirtNoClub = tpl.ShirtNo;
                    }
                    player.TooltipText = $"ID: {player.ID}{Environment.NewLine}Team: {team.TeamName}";
                    //Console.WriteLine($"{team.TeamName} {tpl.OrderInTeam}. {player.Name}");
                    //if (tpl.IsCaptain)
                    //{
                    //    Console.WriteLine($"{player.Name} is captain of {team.TeamName}");
                    //}
                }
            }

            //var nations = Nationalities.GetAll();

            //foreach(var player in Players.List)
            //{
            //    var nationName = "";

            //    if (nations.TryGetValue(player.Nationality, out var nationality))
            //    {
            //        nationName = nationality.Name;
            //    }
            //    player.TooltipText = $"Nationality: {nationName}{Environment.NewLine}Age: {player.Age}{Environment.NewLine}Club: {player.Team?.TeamName}";

            //}

            QueuedPlayerSearch();
        }

        public void SavePlayerAssignmentBin(string filename)
        {
            using (FileStream file = new FileStream(filename, FileMode.Create, FileAccess.Write))
            {
                var data = TeamPlayerLinks.ZlibbedData();
                file.Write(data, 0, data.Length);
            }
        }


        public void ReadTacticsBin(string filename)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (FileStream file = new FileStream(filename, FileMode.Open, FileAccess.Read))
                {
                    file.CopyTo(ms);

                    Tactics.Data = ms.ToArray();

                }
            }
        }

        private void Tactics_BinaryReadCompleted(object sender, EventArgs e)
        {
            Console.WriteLine($"Num of tactics: {Tactics.List.Count}");

            //foreach (var tactic in Tactics.List)
            //{
            //    var team = Teams.GetTeam(tactic.TeamID);

            //    if (team != null)
            //    {
            //        team.Tactics.Add(tactic);
            //        Console.WriteLine($"{team.TeamName} tactic id {tactic.TacticsID}");
            //    }
            //}
        }

        private void ReadTacticFormationsBin(string filename)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (FileStream file = new FileStream(filename, FileMode.Open, FileAccess.Read))
                {
                    file.CopyTo(ms);

                    TacticFormations.Data = ms.ToArray();

                }
            }
        }

        private void TacticFormations_BinaryReadCompleted(object sender, EventArgs e)
        {
            Console.WriteLine($"Num of tactic formatios: {TacticFormations.List.Count}");

            foreach (var formation in TacticFormations.List)
            {
                var tactic = Tactics.List.FirstOrDefault(t => t.TacticsID == formation.TacticsID);

                if (tactic != null)
                {
                    tactic.Formations.Add(formation);

                    var team = Teams.GetTeam(tactic.TeamID);

                    if (team != null)
                    {
                        //Console.WriteLine($"{team.TeamName} formation idx {formation.FormationIndex}");

                        if (!team.Tactics.Contains(tactic))
                        {
                            team.Tactics.Add(tactic);
                        }
                    }
                }
            }
        }

        private void lstTeams_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            QueuedPlayerSearch();

        }

        private void txtPesTeamSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            TeamsView.Refresh();
        }

        private void lstPesTeams_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            TeamPlayersViewRefresh();
            cmbTransferTeamFromGlobal.SelectedItem = lstPesTeams.SelectedItem;
            //Console.WriteLine((lstPesTeams.SelectedItem as Team)?.IsNationalTeam);

            var team = lstPesTeams.SelectedItem as Team;

            if (team != null)
            {
                if (team.IsNationalTeam)
                {
                    if (team.Tactics.Count > 0)
                    {
                        for (int i = 0; i < 11; i++)
                        {
                            team.Players[i].PosNational = (int)(team.Tactics[0].Formations[i]?.PositionRole);
                        }
                    }
                    colPesPlayersShirtNoClub.Visibility = Visibility.Collapsed;
                    colPesPlayersPosClub.Visibility = Visibility.Collapsed;
                    colPesPlayersShirtNoNational.Visibility = Visibility.Visible;
                    colPesPlayersPosNational.Visibility = Visibility.Visible;
                }
                else
                {
                    if (team.Tactics.Count > 0)
                    {
                        for (int i = 0; i < 11; i++)
                        {
                            team.Players[i].PosClub = (int)(team.Tactics[0].Formations[i]?.PositionRole);
                        }
                    }
                    colPesPlayersShirtNoClub.Visibility = Visibility.Visible;
                    colPesPlayersPosClub.Visibility = Visibility.Visible;
                    colPesPlayersShirtNoNational.Visibility = Visibility.Collapsed;
                    colPesPlayersPosNational.Visibility = Visibility.Collapsed;
                }
                Console.WriteLine($"{team.TeamName} tactics count: {team.Tactics.Count}");
            }
        }

        private void txtPesPlayerSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            TeamPlayersViewRefresh();
        }

        private void QueuedPlayerSearch()
        {
            int i = 0;
            while (bwPlayerSearch.IsBusy)
            {
                //Application.DoEvents();
                Thread.Sleep(100);
                if (i++ >= 5)
                {
                    return;
                }
            }

            bwPlayerSearch.RunWorkerAsync();
        }
        private void txtSofaTeamSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            SofaTeamsView.Refresh();
        }

        private void lstSofaTeams_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SofaPlayersView.Refresh();
        }

        private void txtSofaPlayerSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            SofaPlayersView.Refresh();
        }

        private async void lstSofaTeams_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var sofateam = lstSofaTeams.SelectedItem as SofaTeam;

            //if (sofateam != null)
            //{
            //    var t = await SofaApi.ReadTeam(sofateam.ID);

            //}
        }

        private void btnTransferPlayer_Click(object sender, RoutedEventArgs e)
        {
            var player = lstPesPlayers.SelectedItem as Player;
            var team = cmbTransferTeam.SelectedItem as Team;

            if (lstPesPlayers.SelectedItems.Count == 1 && team != null)
            {
                player.Team.RemovePlayer(player);
                team.AddPlayer(player);
                TeamPlayersViewRefresh();
                ShowTimedMessage("Player transfer is successful.");
            }
            else if (team == null)
            {
                ShowTimedMessage("No valid team for transfer.");
            }
            else if (lstPesPlayers.SelectedItems.Count == 0)
            {
                ShowTimedMessage("No player is chosen for transfer.");
            }
            else if (lstPesPlayers.SelectedItems.Count > 1)
            {
                ShowTimedMessage("Only a single player can be transfered at a time.");
            }
        }

        private void lstPesPlayers_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            var player = lstPesPlayers.SelectedItem as Player;

            if (e.Key == Key.Delete && player != null)
            {
                var team = lstPesTeams.SelectedItem as Team;

                if (team != null)
                {
                    team.RemovePlayer(player);
                }

                TeamPlayersViewRefresh();
            }
        }

        private void txtAllPesPlayerSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            QueuedPlayerSearch();
        }

        private void btnTransferPlayerFromGlobal_Click(object sender, RoutedEventArgs e)
        {
            var player = lstAllPesPlayers.SelectedItem as Player;
            var team = cmbTransferTeamFromGlobal.SelectedItem as Team;

            if (lstAllPesPlayers.SelectedItems.Count == 1 && team != null)
            {
                if (player.Team != null)
                {
                    player.Team.RemovePlayer(player);
                }

                team.AddPlayer(player);
                TeamPlayersViewRefresh();
                ShowTimedMessage("Player transfer is successful.");
            }
            else if (team == null)
            {
                ShowTimedMessage("No valid team for transfer.");
            }
            else if (lstAllPesPlayers.SelectedItems.Count == 0)
            {
                ShowTimedMessage("No player is chosen for transfer.");
            }
            else if (lstAllPesPlayers.SelectedItems.Count > 1)
            {
                ShowTimedMessage("Only a single player can be transfered at a time.");
            }
        }

        private void btnSaveSquads_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show($"Save changes?{Environment.NewLine}Directory: {Path.GetDirectoryName(PlayerBinPath)}",
                "Save changes", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            {
                SavePlayerAssignmentBin(PlayerAssignmentBinPath);
                ShowTimedMessage("All changes are saved successfully.");
            }
        }

        private void lstPesPlayers_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Link;

            e.Handled = true;
            return;
        }

        private void lstPesPlayers_DragOver(object sender, DragEventArgs e)
        {

        }

        private void lstPesPlayers_DragLeave(object sender, DragEventArgs e)
        {

        }

        private void lstPesPlayers_Drop(object sender, DragEventArgs e)
        {
            if (rowIndex < 0)
                return;
            int index = GetCurrentRowIndex(e.GetPosition);
            if (index < 0)
                return;
            if (index == rowIndex)
                return;

            var sourceItem = lstPesPlayers.Items[rowIndex] as Player;
            var targetItem = lstPesPlayers.Items[index] as Player;

            var team = lstPesTeams.SelectedItem as Team;

            if (team != null && targetItem != null && sourceItem != null && targetItem != sourceItem)
            {
                var tplS = team.IsNationalTeam ? TeamPlayerLinks.GetNationalTeamLinkForPlayer(sourceItem) :
                    TeamPlayerLinks.GetClubTeamLinkForPlayer(sourceItem);

                var tplT = team.IsNationalTeam ? TeamPlayerLinks.GetNationalTeamLinkForPlayer(targetItem) :
                    TeamPlayerLinks.GetClubTeamLinkForPlayer(targetItem);

                tplS.PlayerID = (int)targetItem.ID;
                tplS.ShirtNo = (byte)(team.IsNationalTeam ? targetItem.ShirtNoClub : targetItem.ShirtNoNational);

                tplT.PlayerID = (int)sourceItem.ID;
                tplT.ShirtNo = (byte)(team.IsNationalTeam ? sourceItem.ShirtNoClub : sourceItem.ShirtNoNational);

                TeamPlayersViewRefresh();
            }
        }

        private void lstPesPlayers_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                rowIndex = GetCurrentRowIndex(e.GetPosition);
                if (rowIndex < 0)
                    return;
                DataGrid grid = e.Source as DataGrid;
                DataGridRow row = Extensions.GetParent<DataGridRow>(e.OriginalSource as DependencyObject);
                grid.SelectedIndex = rowIndex;

                if (grid != null && row != null)
                {
                    e.Handled = true;
                    if (System.Windows.DragDrop.DoDragDrop(grid, row, DragDropEffects.Link) != DragDropEffects.None)
                    {

                    }
                }
            }
        }
        public delegate Point GetPosition(IInputElement element);
        private bool GetMouseTargetRow(Visual theTarget, GetPosition position)
        {
            Rect rect = VisualTreeHelper.GetDescendantBounds(theTarget);
            Point point = position((IInputElement)theTarget);
            return rect.Contains(point);
        }
        private DataGridRow GetRowItem(int index)
        {
            if (lstPesPlayers.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated)
                return null;

            return lstPesPlayers.ItemContainerGenerator.ContainerFromIndex(index) as DataGridRow;
        }
        private int GetCurrentRowIndex(GetPosition pos)
        {
            int curIndex = -1;
            for (int i = 0; i < lstPesPlayers.Items.Count; i++)
            {
                DataGridRow itm = GetRowItem(i);
                if (GetMouseTargetRow(itm, pos))
                {
                    curIndex = i;
                    break;
                }
            }
            return curIndex;
        }


        void IDropTarget.DragOver(IDropInfo dropInfo)
        {
            if (dropInfo.Data is Player && dropInfo.TargetItem is Player)
            {
                dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
                dropInfo.Effects = DragDropEffects.Copy;
            }
        }
        void IDropTarget.Drop(IDropInfo dropInfo)
        {
            Player sourceItem = dropInfo.Data as Player;
            Player targetItem = dropInfo.TargetItem as Player;
            var team = lstPesTeams.SelectedItem as Team;

            if (team != null && targetItem != null && sourceItem != null && targetItem != sourceItem)
            {
                var tplS = team.IsNationalTeam ? TeamPlayerLinks.GetNationalTeamLinkForPlayer(sourceItem) :
                    TeamPlayerLinks.GetClubTeamLinkForPlayer(sourceItem);

                var tplT = team.IsNationalTeam ? TeamPlayerLinks.GetNationalTeamLinkForPlayer(targetItem) :
                    TeamPlayerLinks.GetClubTeamLinkForPlayer(targetItem);

                var firstPlayerID = tplS.PlayerID;
                tplS.PlayerID = tplT.PlayerID;
                tplT.PlayerID = firstPlayerID;

                if (team.IsNationalTeam)
                {
                    var firstPlayerPosition = sourceItem.PosNational;
                    sourceItem.PosNational = targetItem.PosNational;
                    targetItem.PosNational = firstPlayerPosition;
                }
                else
                {
                    var firstPlayerPosition = sourceItem.PosClub;
                    sourceItem.PosClub = targetItem.PosClub;
                    targetItem.PosClub = firstPlayerPosition;
                }

                TeamPlayersViewRefresh();
            }
        }

        void IDropTarget.DragEnter(IDropInfo dropInfo)
        {
            
        }

        void IDropTarget.DragLeave(IDropInfo dropInfo)
        {
            
        }

        private void btnUnzlibDir_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show($"All files in following directory will be unzlibbed. Are you sure?{Environment.NewLine}{Properties.Settings.Default.LastDir}", 
                "Unzlib", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            {
                UnZlibDirectory(Properties.Settings.Default.LastDir);
            }
        }

        private void btnReload_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Do you want to reload files?", "Reload", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
            {
                LoadFilesAsync();
                ShowTimedMessage("All files are reloaded");
            }
        }

        private void cmbSofaLeague_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = (sender as ComboBox).SelectedItem as ComboBoxItem;
            var tId = Convert.ToInt32(item?.Tag);

            if (item != null)
            {
                var bw = new BackgroundWorker();
                bw.DoWork += (object s, DoWorkEventArgs doe) =>
                {
                    ShowTimedMessage("Fetching league teams...");

                    prgStatus.Dispatcher.Invoke(() =>
                    {
                        prgStatus.IsIndeterminate = true;
                    });

                    cmbSofaLeague.Dispatcher.Invoke(() => cmbSofaLeague.IsEnabled = false);

                    lock(SofaTeamsLock)
                    {
                        SofaApi.ReadTournament2(tId);
                    }
                };
                //var teams = await tou;

                //foreach (var sofaTeam in SofaApi.Teams.ToList())
                //{
                //    var levenshteinDistances = Pes2021Api.Teams.List.Select(t => Fastenshtein.Levenshtein.Distance(sofaTeam.TeamName, t.TeamName)).ToList();
                //    var zipped = Pes2021Api.Teams.List
                //        .Zip(levenshteinDistances, (Team, Difference) => new { Team, Difference })
                //        .OrderBy(z => z.Difference).ToList();

                //    //Console.WriteLine($"\nSofa-> {sofaTeam.TeamName}\t(First 3):\n{new string('-', 20)}");

                //    //foreach(var z in zipped.Take(3))
                //    //{
                //    //    Console.WriteLine($"{z.Team}: {z.Difference}%");
                //    //}
                //    var pesTeam = zipped.First().Team;

                //    Console.WriteLine($"\nSofa-> {sofaTeam.TeamName} ~= Pes-> {pesTeam.TeamName}\n{new string('-', 20)}");

                //    foreach (var sofaPlayer in sofaTeam.Players)
                //    {
                //        var levenshteinDistances2 = pesTeam.Players.Select(p =>
                //            Fastenshtein.Levenshtein.Distance(PesifyString(sofaPlayer.PlayerName), PesifyString(p.PlayerName))).ToList();

                //        var zipped2 = pesTeam.Players
                //            .Zip(levenshteinDistances2, (Player, Difference) => new { Player, Difference })
                //            .OrderBy(z => z.Difference).ToList();

                //        if (zipped2.First().Difference <= 1)
                //        {
                //            var pesPlayer = zipped2.First().Player;
                //            Console.WriteLine($"Sofa-> {sofaPlayer.PlayerName} ~= Pes-> {pesPlayer.PlayerName} [{zipped2.First().Difference}]");
                //        }
                //        else
                //        {
                //            var levenshteinDistances3 = Players.List.Select(p =>
                //                Fastenshtein.Levenshtein.Distance(PesifyString(sofaPlayer.PlayerName), PesifyString(p.PlayerName))).ToList();

                //            var zipped3 = Players.List
                //                .Zip(levenshteinDistances3, (Player, Difference) => new { Player, Difference })
                //                .OrderBy(z => z.Difference).ToList();

                //            var pesPlayer = zipped3.First().Player;

                //            if (zipped3.First().Difference <= 1)
                //            {
                //                Console.WriteLine($"Sofa-> {sofaPlayer.PlayerName} ~= Pes ({pesPlayer.Team?.TeamName})-> {pesPlayer.PlayerName} [{zipped3.First().Difference}]");
                //            }
                //            else
                //            {
                //                Console.WriteLine($"Sofa-> {sofaPlayer.PlayerName} ~= Pes-> NO MATCH (Nearest: {pesPlayer.PlayerName} [{zipped3.First().Difference}])");
                //            }
                //        }
                //    }
                //}

                //var s = SofaApi.Serialize();
                bw.RunWorkerCompleted += (object s, RunWorkerCompletedEventArgs rwce) =>
                {
                    lstSofaTeams.Dispatcher.Invoke(() => SofaTeamsView.Refresh());
                    cmbSofaLeague.Dispatcher.Invoke(() => cmbSofaLeague.IsEnabled = true);

                    prgStatus.Dispatcher.Invoke(() =>
                    {
                        prgStatus.IsIndeterminate = false;
                    });
                };
                //}
                bw.RunWorkerAsync();
            }
        }

        private void Bw_DoWork(object sender, DoWorkEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void lstPesPlayers_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            switch (e.Row.GetIndex())
            {
                case int i when i < 11:
                    e.Row.Background = new SolidColorBrush(Colors.Cornsilk);
                    break;
                default:
                    e.Row.Background = new SolidColorBrush(Colors.BlanchedAlmond);
                    break;
            }

            return;
            DataRowView item = e.Row.Item as DataRowView;
            if (item != null)
            {
                DataRow row = item.Row; 

                // Access cell values values if needed...
                // var colValue = row["ColumnName1]";
                // var colValue2 = row["ColumName2]";

                // Set the background color of the DataGrid row based on whatever data you like from 
                // the row.
                e.Row.Background = new SolidColorBrush(Colors.BlanchedAlmond);
            }
        }

        private void ShowTimedMessage(string msg, int seconds = 3)
        {
            if (!messageTimer.IsEnabled)
            {
                txtStatus.Dispatcher.Invoke(() => txtStatus.Text = msg);

                messageTimer = new DispatcherTimer();
                messageTimer.Interval = TimeSpan.FromSeconds(seconds);

                messageTimer.Tick += (o, e) =>
                {
                    txtStatus.Dispatcher.Invoke(() => txtStatus.Text = "");
                };

                messageTimer.Start();
            }
        }
    }
}
