using InstallerUI.Bootstrapper;
using InstallerUI.Interfaces;
using Microsoft.Practices.Prism.Commands;
using Microsoft.Practices.Prism.Mvvm;
using Microsoft.Tools.WindowsInstallerXml.Bootstrapper;
using Newtonsoft.Json;
using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows.Input;

namespace InstallerUI.ViewModel
{
    [Export]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public class InstallerUIWindowViewModel : BindableBase
    {
        private readonly int port = 3307;
        private BootstrapperApplication bootstrapper;
        private Engine engine;
        private readonly BootstrapperBundleData bootstrapperBundleData;

        [Import]
        private IInteractionService interactionService = null;


        [Import(typeof(IMySQLService))]
        private IMySQLService MySQLService { get; set; }

        #region Properties for data binding
        private DelegateCommand InstallCommandValue;
        public ICommand InstallCommand { get { return InstallCommandValue; } }

        private DelegateCommand UninstallCommandValue;
        public ICommand UninstallCommand { get { return UninstallCommandValue; } }

        private readonly DelegateCommand CancelCommandValue;
        public ICommand CancelCommand { get { return CancelCommandValue; } }

        private InstallationStatus StatusValue;
        public InstallationStatus Status
        {
            get => StatusValue;
            set
            {
                SetProperty(ref StatusValue, value);
                InstallCommandValue.RaiseCanExecuteChanged();
                UninstallCommandValue.RaiseCanExecuteChanged();
            }
        }
        
        private string InstallUpgradeTextValue = "INSTALL";
        public string InstallUpgradeText
        {
            get => InstallUpgradeTextValue;
            set
            {
                SetProperty(ref InstallUpgradeTextValue, value);
            }
        }

        private bool DowngradeValue;
        public bool Downgrade
        {
            get => DowngradeValue;
            set => SetProperty(ref DowngradeValue, value);
        }

        private int LocalProgressValue;
        public int LocalProgress
        {
            get => LocalProgressValue;
            set => SetProperty(ref LocalProgressValue, value);
        }

        private int GlobalProgressValue;
        public int GlobalProgress
        {
            get => GlobalProgressValue;
            set => SetProperty(ref GlobalProgressValue, value);
        }

        private string ProgressValue;
        public string Progress
        {
            get => ProgressValue;
            set => SetProperty(ref ProgressValue, value);
        }

        private string CurrentPackageValue;
        public string CurrentPackage
        {
            get => CurrentPackageValue;
            set => SetProperty(ref CurrentPackageValue, value);
        }

        private bool InstallingValue;
        public bool Installing
        {
            get => InstallingValue;
            set
            {
                SetProperty(ref InstallingValue, value);
                InstallCommandValue.RaiseCanExecuteChanged();
                UninstallCommandValue.RaiseCanExecuteChanged();
            }
        }

        private bool isCancelledValue;
        public bool IsCancelled
        {
            get => isCancelledValue;
            set
            {
                SetProperty(ref isCancelledValue, value);
            }
        }

        private string CurrentActionValue;
        public string CurrentAction
        {
            get => CurrentActionValue;
            set
            {
                SetProperty(ref CurrentActionValue, value);
            }
        }        
        #endregion

        [ImportingConstructor]
        public InstallerUIWindowViewModel(BootstrapperApplication bootstrapper, Engine engine)
        {
            bootstrapperBundleData = new BootstrapperBundleData();
            this.bootstrapper = bootstrapper;
            this.engine = engine;

            
            InstallCommandValue = new DelegateCommand(
                () => engine.Plan(LaunchAction.Install),
                () => !Installing && Status == InstallationStatus.DetectedAbsent);
            UninstallCommandValue = new DelegateCommand(
                () => engine.Plan(LaunchAction.Uninstall),
                () => !Installing && Status == InstallationStatus.DetectedPresent);
            CancelCommandValue = new DelegateCommand(
                () => IsCancelled = true);



            bootstrapper.DetectBegin += (_, ea) =>
            {
                LogEvent("DetectBegin", ea);
                CurrentAction = ea.Installed ? "Preparing for software uninstall" : "Preparing for software install";
                interactionService.RunOnUIThread(
                    () =>
                        {
                            Status = ea.Installed ? InstallationStatus.DetectedPresent : InstallationStatus.DetectedAbsent;
                            InstallUpgradeText = ea.Installed ? "UPGRADE" : "INSTALL";
                        }
            };

            bootstrapper.DetectRelatedBundle += (_, ea) =>
            {
                LogEvent("DetectRelatedBundle", ea);

                
                interactionService.RunOnUIThread(() => Downgrade |= ea.Operation == RelatedOperation.Downgrade);
            };

            bootstrapper.DetectComplete += (s, ea) =>
            {
                LogEvent("DetectComplete");
                DetectComplete(s, ea);
            };

            bootstrapper.PlanComplete += (_, ea) =>
            {
                LogEvent("PlanComplete", ea);

                
                if (ea.Status >= 0)
                {
                    engine.Apply(interactionService.GetMainWindowHandle());
                }
            };

            bootstrapper.ApplyBegin += (_, ea) =>
            {
                LogEvent("ApplyBegin");

                
                interactionService.RunOnUIThread(() => Installing = true);
            };

            bootstrapper.ExecutePackageBegin += (_, ea) =>
            {
                LogEvent("ExecutePackageBegin", ea);
                CurrentAction = this.Status == InstallationStatus.DetectedAbsent ? "We are installing software" : "We are uninstalling software";
                
                interactionService.RunOnUIThread(() =>
                CurrentPackage = String.Format("Current package: {0}",
                bootstrapperBundleData.Data.Packages.Where(p => p.Id == ea.PackageId).FirstOrDefault().DisplayName));
            };

            bootstrapper.ExecutePackageComplete += (_, ea) =>
            {
                if (Status == InstallationStatus.DetectedAbsent && IsCancelled != true)
                {
                    if (ea.PackageId == "MySQL")
                    {
                        CurrentAction = "Installing & Configuring MySQL Server";
                        MySQLService.InitServer(port);
                    }
                }

                LogEvent("ExecutePackageComplete", ea);
                
                interactionService.RunOnUIThread(() => CurrentPackage = string.Empty);
            };

            bootstrapper.ExecuteProgress += (_, ea) =>
            {
                LogEvent("ExecuteProgress", ea);
                if (IsCancelled == true)
                {
                    ea.Result = Result.Abort;
                }
                Progress = String.Format("Progress: {0}{1}", ea.OverallPercentage.ToString(), "%");

                
                interactionService.RunOnUIThread(() =>
                {
                    LocalProgress = ea.ProgressPercentage;
                    GlobalProgress = ea.OverallPercentage;
                });
            };

            bootstrapper.ApplyComplete += (_, ea) =>
            {
                LogEvent("ApplyComplete", ea);
                interactionService.CloseUIAndExit();
            };
        }

        private void DetectComplete(object sender, DetectCompleteEventArgs e)
        {
            if (LaunchAction.Uninstall == bootstrapper.Command.Action)
            {
                engine.Log(LogLevel.Verbose, "Invoking automatic plan for uninstall");
                engine.Plan(LaunchAction.Uninstall);
            }
        }

        private void LogEvent(string eventName, EventArgs arguments = null)
        {
            engine.Log(
                LogLevel.Verbose,
                arguments == null ? string.Format("EVENT: {0}", eventName)
                                    :
                                    string.Format("EVENT: {0} ({1})",
                                                  eventName,
                                                  JsonConvert.SerializeObject(arguments))
            );
        }
    }
}
