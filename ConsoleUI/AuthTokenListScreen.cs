using System;
using System.ComponentModel;
using System.Collections.Generic;
using CKAN.ConsoleUI.Toolkit;
using Autofac;
using CKAN.Configuration;

namespace CKAN.ConsoleUI
{

    /// <summary>
    /// Screen for display and editing of authentication tokens.
    /// </summary>
    public class AuthTokenScreen : ConsoleScreen {

        /// <summary>
        /// Initialize the screen.
        /// </summary>
        public AuthTokenScreen() : base()
        {
            mainMenu = new ConsolePopupMenu(new List<ConsoleMenuOption>() {
                new ConsoleMenuOption("Make a GitHub API token", "",
                    "Open the web page for creating GitHub API authentication tokens",
                    true, openGitHubURL)
            });

            AddObject(new ConsoleLabel(
                1, 2, -1,
                () => "Authentication tokens for downloads:"
            ));

            tokenList = new ConsoleListBox<string>(
                1, 4, -1, -2,
                new List<string>(ServiceLocator.Container.Resolve<IConfiguration>().GetAuthTokenHosts()),
                new List<ConsoleListBoxColumn<string>>() {
                    new ConsoleListBoxColumn<string>() {
                        Header   = "Host",
                        Width    = 20,
                        Renderer = (string s) => s
                    },
                    new ConsoleListBoxColumn<string>() {
                        Header   = "Token",
                        Width    = 50,
                        Renderer = (string s) => {
                            string token;
                            return ServiceLocator.Container.Resolve<IConfiguration>().TryGetAuthToken(s, out token)
                                ? token
                                : missingTokenValue;
                        }
                    }
                },
                0, 0, ListSortDirection.Descending
            );
            AddObject(tokenList);

            AddObject(new ConsoleLabel(
                3, -1, -1,
                () => "NOTE: These values are private! Do not share screenshots of this screen!",
                null,
                th => th.AlertFrameFg
            ));

            AddTip("Esc", "Back");
            AddBinding(Keys.Escape, (object sender, ConsoleTheme theme) => false);

            tokenList.AddTip("A", "Add");
            tokenList.AddBinding(Keys.A, (object sender, ConsoleTheme theme) => {
                AuthTokenAddDialog ad = new AuthTokenAddDialog();
                ad.Run(theme);
                DrawBackground(theme);
                tokenList.SetData(new List<string>(ServiceLocator.Container.Resolve<IConfiguration>().GetAuthTokenHosts()));
                return true;
            });

            tokenList.AddTip("R", "Remove", () => tokenList.Selection != null);
            tokenList.AddBinding(Keys.R, (object sender, ConsoleTheme theme) => {
                if (tokenList.Selection != null) {
                    ServiceLocator.Container.Resolve<IConfiguration>().SetAuthToken(tokenList.Selection, null);
                    tokenList.SetData(new List<string>(ServiceLocator.Container.Resolve<IConfiguration>().GetAuthTokenHosts()));
                }
                return true;
            });
        }

        /// <summary>
        /// Put CKAN 1.25.5 in top left corner
        /// </summary>
        protected override string LeftHeader()
        {
            return $"CKAN {Meta.GetVersion()}";
        }

        /// <summary>
        /// Put description in top center
        /// </summary>
        protected override string CenterHeader()
        {
            return "Authentication Tokens";
        }

        private bool openGitHubURL(ConsoleTheme theme)
        {
            ModInfoScreen.LaunchURL(theme, githubTokenURL);
            return true;
        }

        private ConsoleListBox<string> tokenList;

        private const           string missingTokenValue = "<ERROR>";
        private static readonly Uri    githubTokenURL    = new Uri("https://github.com/settings/tokens");
    }

}
