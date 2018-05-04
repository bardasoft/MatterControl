﻿/*
Copyright (c) 2018, Lars Brubaker, John Lewin
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies,
either expressed or implied, of the FreeBSD Project.
*/

using System;
using System.Linq;
using MatterHackers.Agg;
using MatterHackers.Agg.Platform;
using MatterHackers.Agg.UI;
using MatterHackers.Localizations;
using MatterHackers.MatterControl.PartPreviewWindow.PlusTab;
using MatterHackers.MatterControl.SlicerConfiguration;
using MatterHackers.VectorMath;

namespace MatterHackers.MatterControl.PartPreviewWindow
{
	public class PartPreviewContent : FlowLayoutWidget
	{
		private EventHandler unregisterEvents;
		private ChromeTab printerTab = null;
		private ChromeTabs tabControl;

		public PartPreviewContent()
			: base(FlowDirection.TopToBottom)
		{
			var printer = ApplicationController.Instance.ActivePrinter;
			var theme = ApplicationController.Instance.Theme;

			this.AnchorAll();

			var extensionArea = new FlowLayoutWidget();

			tabControl = new ChromeTabs(extensionArea, theme)
			{
				VAnchor = VAnchor.Stretch,
				HAnchor = HAnchor.Stretch,
				BackgroundColor = ActiveTheme.Instance.PrimaryBackgroundColor,
				BorderColor = theme.MinimalShade,
				Border = new BorderDouble(left: 1),
				NewTabPage = () =>
				{
					return new PlusTabPage(this, theme);
				}
			};
			tabControl.TabBar.BackgroundColor = theme.ActiveTabBarBackground;

			tabControl.ActiveTabChanged += (s, e) =>
			{
				if (this.tabControl.ActiveTab?.TabContent is PartTabPage tabPage)
				{
					var dragDropData = ApplicationController.Instance.DragDropData;

					// Set reference on tab change
					dragDropData.View3DWidget = tabPage.view3DWidget;
					dragDropData.SceneContext = tabPage.sceneContext;
				}
			};

			tabControl.TabBar.BorderColor = theme.ActiveTabColor;
			tabControl.TabBar.Padding = new BorderDouble(top: 4);
			//tabControl.TabBar.Border = new BorderDouble(bottom: 2);

			Color selectedTabColor = ActiveTheme.Instance.TabLabelSelected;

			// add in a what's new button
			Button seeWhatsNewButton = theme.LinkButtonFactory.Generate("What's New...".Localize());
			seeWhatsNewButton.Name = "What's New Link";
			seeWhatsNewButton.ToolTipText = "See what's new in this version of MatterControl".Localize();
			seeWhatsNewButton.VAnchor = VAnchor.Center;
			seeWhatsNewButton.Margin = new Agg.BorderDouble(10, 0);
			seeWhatsNewButton.Click += (s, e) => UiThread.RunOnIdle(() =>
			{
				DialogWindow.Show(new DesignSpaceGuid("What's New Tab", ""));
			});

			tabControl.TabBar.ActionArea.AddChild(seeWhatsNewButton);

			// add in the update available button
			Button updateAvailableButton = theme.LinkButtonFactory.Generate("Update Available".Localize());

			// make the function inline so we don't have to create members for the buttons
			EventHandler SetLinkButtonsVisability = (s, e) =>
			{
				if(UserSettings.Instance.HasLookedAtWhatsNew())
				{
					// hide it
					seeWhatsNewButton.Visible = false;
				}

				if (UpdateControlData.Instance.UpdateStatus == UpdateControlData.UpdateStatusStates.UpdateAvailable)
				{
					updateAvailableButton.Visible = true;
					// if we are going to show the update link hide the whats new link no matter what
					seeWhatsNewButton.Visible = false;
				}
				else
				{
					updateAvailableButton.Visible = false;
				}
			};

			UserSettings.Instance.Changed += SetLinkButtonsVisability;
			Closed += (s, e) => UserSettings.Instance.Changed -= SetLinkButtonsVisability;

			updateAvailableButton.Name = "Update Available Link";
			SetLinkButtonsVisability(this, null);
			updateAvailableButton.ToolTipText = "There is a new update available for download".Localize();
			updateAvailableButton.VAnchor = VAnchor.Center;
			updateAvailableButton.Margin = new Agg.BorderDouble(10, 0);
			updateAvailableButton.Click += (s, e) => UiThread.RunOnIdle(() =>
			{
				UiThread.RunOnIdle(() =>
				{
					UpdateControlData.Instance.CheckForUpdate();
					DialogWindow.Show<CheckForUpdatesPage>();
				});
			});

			tabControl.TabBar.ActionArea.AddChild(updateAvailableButton);

			UpdateControlData.Instance.UpdateStatusChanged.RegisterEvent(SetLinkButtonsVisability, ref unregisterEvents);

			this.AddChild(tabControl);

			ActiveSliceSettings.SettingChanged.RegisterEvent((s, e) =>
			{
				if (e is StringEventArgs stringEvent
					&& stringEvent.Data == SettingsKey.printer_name
					&& printerTab != null)
				{
					printerTab.Text = ActiveSliceSettings.Instance.GetValue(SettingsKey.printer_name);
				}

			}, ref unregisterEvents);

			ApplicationController.Instance.NotifyPrintersTabRightElement(extensionArea);

			// Show fixed start page
			tabControl.AddTab(
				new ChromeTab("Start".Localize(),  tabControl, tabControl.NewTabPage(), theme, hasClose: false)
				{
					MinimumSize = new Vector2(0, theme.TabButtonHeight),
					Name = "Start Tab",
					Padding = new BorderDouble(15, 0)
				});

			// Add a tab for the current printer
			if (ActiveSliceSettings.Instance.PrinterSelected)
			{
				this.CreatePrinterTab(printer, theme, printer.Settings.GetValue(SettingsKey.printer_name));
			}

			// Restore active tabs
			foreach (var bed in ApplicationController.Instance.Workspaces)
			{
				this.CreatePartTab("New Part", bed, theme);
			}
		}

		internal ChromeTab CreatePrinterTab(PrinterConfig printer, ThemeConfig theme, string tabTitle)
		{
			printerTab = new ChromeTab(
				tabTitle,
				tabControl,
				new PrinterTabPage(printer, theme, tabTitle.ToUpper()),
				theme,
				tabImageUrl: ApplicationController.Instance.GetFavIconUrl(oemName: printer.Settings.GetValue(SettingsKey.make)))
			{
				Name = "3D View Tab",
				MinimumSize = new Vector2(120, theme.TabButtonHeight)
			};

			tabControl.AddTab(printerTab);

			return printerTab;
		}

		public ChromeTab CreatePartTab(string tabTitle, BedConfig sceneContext, ThemeConfig theme)
		{
			var partTab = new ChromeTab(
				tabTitle,
				tabControl,
				new PartTabPage(null, sceneContext, theme, "xxxxx"),
				theme,
				AggContext.StaticData.LoadIcon("part.png"))
			{
				Name = "newPart" + tabControl.AllTabs.Count(),
				MinimumSize = new Vector2(120, theme.TabButtonHeight)
			};

			tabControl.AddTab(partTab);

			return partTab;
		}

		public override void OnClosed(ClosedEventArgs e)
		{
			unregisterEvents?.Invoke(this, null);
			base.OnClosed(e);
		}
	}
}