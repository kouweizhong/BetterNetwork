﻿using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using BetterNetwork.Models;
using Caliburn.Micro;
using Microsoft.Win32;
using System.Linq;
using System.Text;
using System.Security;
using System;

namespace BetterNetwork.ViewModels
{
    [Export(typeof(AppViewModel))]
    public partial class AppViewModel : PropertyChangedBase
    {
        #region Properties
        private ObservableCollection<InterfaceProfile> _interfaceProfiles; 
        public ObservableCollection<InterfaceProfile> InterfaceProfiles
        {
            get { return _interfaceProfiles; } 
            set
            {
                _interfaceProfiles = value;
                NotifyOfPropertyChange(() => InterfaceProfiles);
            }
        }

        private ObservableCollection<NetworkProfile> _networkProfiles;
        public ObservableCollection<NetworkProfile>  NetworkProfiles
        {
            get { return _networkProfiles; }
            set
            {
                _networkProfiles = value; 
                NotifyOfPropertyChange(() => NetworkProfiles);
            }
        }

        private ObservableCollection<InterfaceProfile> ToDeleteInterfaces { get; set; }
        private ObservableCollection<NetworkProfile> ToDeleteNetworks { get; set; }

        private bool _sixtyFourBitChecked;
        public bool SixtyFourBitChecked
        {
            get { return _sixtyFourBitChecked; } 
            set { _sixtyFourBitChecked = value; NotifyOfPropertyChange(() => SixtyFourBitChecked); }
        }

        private bool _thirtyTwoBitChecked;
        public bool ThirtyTwoBitChecked
        {
            get { return _thirtyTwoBitChecked; }
            set { _thirtyTwoBitChecked = value; NotifyOfPropertyChange(() => ThirtyTwoBitChecked); }
        }
        #endregion

        private readonly IWindowManager _windowManager;
        [ImportingConstructor]
        public AppViewModel(IWindowManager windowManager)
        {
            _windowManager = windowManager;
            InterfaceProfiles = new ObservableCollection<InterfaceProfile>();
            NetworkProfiles = new ObservableCollection<NetworkProfile>();
            ToDeleteInterfaces = new ObservableCollection<InterfaceProfile>();
            ToDeleteNetworks = new ObservableCollection<NetworkProfile>();

            SixtyFourBitChecked = true;
        }

        #region Network Interfaces Profiles
        public void GetInterfaceProfiles()
        {
            try
            {
                var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
                                                       SixtyFourBitChecked
                                                           ? RegistryView.Registry64
                                                           : RegistryView.Registry32);
                var interfaces = registry.OpenSubKey(@"SOFTWARE\Microsoft\WlanSvc\Interfaces\");

                if (interfaces != null)
                {
                    foreach (var subkey in interfaces.GetSubKeyNames())
                    {
                        var profile = interfaces.OpenSubKey(subkey);
                        var items = (string[]) profile.GetValue("ProfileList");
                        
                        if (items != null)
                        {
                            foreach (var item in items)
                            {
                                var interfaceProfile = GetInterfaceProfileMetadata(subkey, item);
                                InterfaceProfiles.Add(interfaceProfile);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        private InterfaceProfile GetInterfaceProfileMetadata(string interfaceGuid, string profileGuid)
        {
            var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
                                                       SixtyFourBitChecked
                                                           ? RegistryView.Registry64
                                                           : RegistryView.Registry32);

            var profile = registry.OpenSubKey(@"SOFTWARE\Microsoft\WlanSvc\Interfaces\" + interfaceGuid + @"\Profiles\" + profileGuid + @"\Metadata");
            
            if (profile != null)
            {
                var channel = (byte[])profile.GetValue("Channel Hints");
                if (channel != null)
                {
                    var name = ExtractNetworkNames(channel);

                    return new InterfaceProfile
                        {
                            Name = name, 
                            RegistryPath = profile.Name,
                            InterfaceGuid = interfaceGuid,
                            ProfileGuid = profileGuid
                        };
                }
            }

            return null;
        }

        public void DeleteInterfaceProfiles(InterfaceProfile profile)
        {
            // TODO: Implement delete subkeytree
            try
            {
                // Delete from registry
                var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, SixtyFourBitChecked ? RegistryView.Registry64 : RegistryView.Registry32);
                var path = profile.RegistryPath.Substring(registry.Name.Length + 1);
                registry.DeleteSubKeyTree(path);

                // Then delete from Network Profiles collection so view get updated
                InterfaceProfiles.Remove(profile);
            }
            catch (Exception ex)
            {
                if (ex is SecurityException || ex is UnauthorizedAccessException)
                    MessageBox.Show(ex.Message + " Please run as administrator.", "Admin rights required");
                if (ex is ArgumentException || ex is NullReferenceException)
                    MessageBox.Show(ex.Message, "Could not find registry key");
            }
        }
        
        private static string ExtractNetworkNames(byte[] metadata)
        {
            var position = 0;
            for (var i = 4; i < metadata.Length; i++)
            {
                if (metadata[i] == 0)
                {
                    position = i - 4;
                    break;
                }
            }

            return Encoding.ASCII.GetString((byte[])metadata, 4, position);
        }
        #endregion

        public void GetNetworkProfiles()
        {
            try
            {
                var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, SixtyFourBitChecked ? RegistryView.Registry64 : RegistryView.Registry32);
                var unmanageds =
                    registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Signatures\Unmanaged");

                if (unmanageds != null)
                {
                    foreach (var unmanaged in unmanageds.GetSubKeyNames())
                    {
                        var subkey = unmanageds.OpenSubKey(unmanaged);

                        if (subkey != null)
                        {
                            var desc = subkey.GetValue("Description");
                            var guid = subkey.GetValue("ProfileGuid");
                            var signaturePath = unmanageds.Name + "\\" + unmanaged;
                            var profilepath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\Profiles\" + guid;

                            var network = new NetworkProfile()
                                {
                                    Name = (string)desc,
                                    ProfileGuid = (string)guid,
                                    SignatureRegistryPath = signaturePath,
                                    ProfileRegistryPath = profilepath,
                                    ManageType = "Unmanaged"
                                };

                            NetworkProfiles.Add(network);

                            subkey.Close();
                        }
                    }
                    unmanageds.Close();
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        public void DeleteNetworkProfiles(NetworkProfile profile)
        {
            try
            {
                // Delete from registry
                var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, SixtyFourBitChecked ? RegistryView.Registry64 : RegistryView.Registry32);
                registry.DeleteSubKeyTree(profile.ProfileRegistryPath, false);
                registry.DeleteSubKey(profile.SignatureRegistryPath, false);

                // Then delete from Network Profiles collection so view get updated
                NetworkProfiles.Remove(profile);
            }
            catch (Exception ex)
            {
                if (ex is SecurityException || ex is UnauthorizedAccessException)
                    MessageBox.Show(ex.Message + " Please run as administrator.", "Admin rights required");
                if (ex is ArgumentException || ex is NullReferenceException)
                    MessageBox.Show(ex.Message, "Could not find registry key");
            }
        }

        #region Events Handler
        public void InterfaceChecked(RoutedEventArgs e)
        {
            var item = e.Source as CheckBox;
            var profile = item.DataContext as InterfaceProfile;

            if (!ToDeleteInterfaces.Contains(profile))
                ToDeleteInterfaces.Add(profile);
        }

        public void InterfaceUnchecked(RoutedEventArgs e)
        {
            var item = e.Source as CheckBox;

            var profile = InterfaceProfiles.First(x => x.Name == (string)item.Content);
            if (ToDeleteInterfaces.Contains(profile))
                ToDeleteInterfaces.Remove(profile);
        }

        public void NetworkChecked(RoutedEventArgs e)
        {
            var item = e.Source as CheckBox;

            var profile = NetworkProfiles.First(x => x.Name == (string)item.Content);
            if (!ToDeleteNetworks.Contains(profile))
                ToDeleteNetworks.Add(profile);
        }

        public void NetworkUnchecked(RoutedEventArgs e)
        {
            var item = e.Source as CheckBox;

            var profile = NetworkProfiles.First(x => x.Name == (string)item.Content);
            if (ToDeleteNetworks.Contains(profile))
                ToDeleteNetworks.Remove(profile);
        }
        #endregion

        #region Commands
        public void LoadAll()
        {
            // Clear collections so no duplicate
            InterfaceProfiles.Clear();
            NetworkProfiles.Clear();
            ToDeleteInterfaces.Clear();
            ToDeleteNetworks.Clear();

            // Load all interfaces
            GetInterfaceProfiles();
            GetNetworkProfiles();
        }

        public void Delete()
        {
            if (ToDeleteInterfaces.Count != 0)
            {
                foreach (var interfaceProfile in ToDeleteInterfaces)
                {
                    DeleteInterfaceProfiles(interfaceProfile);
                }
            }

            //if (ToDeleteNetworks.Count != 0)
            //{
            //    foreach (var networkProfile in ToDeleteNetworks)
            //    {
            //        DeleteNetworkProfiles(networkProfile);
            //    }
            //}

            // Clear trackers collection
            ToDeleteNetworks.Clear();
            ToDeleteInterfaces.Clear();

            MessageBox.Show("Changes will only take effect after system restart.");
        }

        public void About()
        {
            _windowManager.ShowWindow(new AboutViewModel(_windowManager));
        }

        public void NavigateTo()
        {
            var url =
                @"https://www.paypal.com/cgi-bin/webscr?cmd=_donations&business=XJCQMH4NUALP8&lc=MY&item_name=Shulhi%20Sapli&item_number=betternetwork&currency_code=USD&bn=PP%2dDonationsBF%3abtn_donate_SM%2egif%3aNonHosted";
            Process.Start(new ProcessStartInfo(url));
        }
        #endregion
    }
}
