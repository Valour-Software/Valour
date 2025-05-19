const driver = window.driver.js.driver;
const mobile = !!window.mobile;
console.log('Mobile for tutorial: ', mobile);
export const doChatDrive = () => {
    const chatDriverObj = driver({
        popoverClass: 'tutorial-popover',
        smoothScroll: true,
        animate: true,
        allowClose: false,
        disableActiveInteraction: true,
        steps: [
            {
                popover: {
                    title: 'Welcome to chat!',
                    description: 'You\'ve opened a chat window. This is where you can chat with other users.',
                },
            },
            {
                element: '.textbox-holder .textbox',
                popover: {
                    title: 'Chat Input',
                    description: 'This is the chat input. Type your message here and hit enter (or the send button on mobile) to send it.',
                },
            },
            {
                element: 'img.upload',
                popover: {
                    title: 'Attachments',
                    description: 'Click here to upload images and files to chat. You can also use tenor gifs.',
                },
            },
            {
                element: '.header-row',
                popover: {
                    title: 'Chat Header',
                    description: 'This is the chat header. You can see who is watching the channel, search for messages, and open the member list.',
                },
            },
            mobile ? {
                element: '.sidebar-toggle',
                popover: {
                    title: 'Sidebar Toggle',
                    description: 'You can swipe from the left, or click here, to open the sidebar at any time.',
                },
                onHighlighted: () => {
                    console.log('Tutorial opening sidebar');
                    if (mobile) {
                        window.setSidebarOpen(true);
                    }
                }
            } : {
                element: '.tab-wrapper .tab',
                popover: {
                    title: 'Window Tab',
                    description: 'This is the window tab. You can drag this tab into a floating window or split screen it with other windows. Your layout will save!',
                },
            },
            {
                element: '.tabstrip .bi-chat-left-fill',
                popover: {
                    title: 'Channel list',
                    description: 'Use this tab to view the planet\'s channel list. You can see all open planets, and click to expand or hide them.',
                },
            },
            {
                element: '.sidebar-tabstrip .self-info',
                popover: {
                    title: 'You at a glance',
                    description: 'This is you! Click the gear icon to edit your avatar, change your settings, and customize your profile.',
                },
            }
        ]
    });
    chatDriverObj.drive();
};
export const doStartDrive = () => {
    const startDriverObj = driver({
        popoverClass: 'tutorial-popover',
        smoothScroll: true,
        animate: true,
        allowClose: false,
        disableActiveInteraction: true,
        steps: [
            {
                popover: {
                    title: 'Welcome to Valour!',
                    description: 'This is a tutorial for the Valour platform. Click next to continue.',
                }
            },
            {
                element: '.window',
                popover: {
                    title: 'Home Window',
                    description: 'This is the home window. You can quickly access your joined servers (planets), friends, and find new communities here',
                }
            },
            !mobile ?
                {
                    element: '.tab.add',
                    popover: {
                        title: 'Add Tab / Go Home',
                        description: 'This is the add tab button. Click here to add a new tab. New tabs opened here will bring you to Home.',
                    }
                } : {
                element: '.channel-and-topbar .home',
                popover: {
                    title: 'Home Button',
                    description: 'This is the home button. Click here to go back home.',
                }
            },
            {
                element: '.icon.planet-icon-12215159187308544',
                popover: {
                    title: 'Valour Central',
                    description: 'This is Valour Central, the default planet. You can click here to enter it and chat!',
                }
            },
            mobile ?
                {
                    element: '.sidebar-toggle',
                    popover: {
                        title: 'Sidebar Toggle',
                        description: 'You can swipe from the left, or click here, to open the sidebar at any time.',
                    },
                    onHighlighted: () => {
                        console.log('Tutorial opening sidebar');
                        if (mobile) {
                            window.setSidebarOpen(true);
                        }
                    }
                } : {
                element: '.version .logo',
                popover: {
                    title: 'Valour Logo',
                    description: 'This is the Valour logo. Click here to reset your window layout at any time. To the right, you can find the version.',
                },
            },
            {
                element: '.tabstrip',
                popover: {
                    title: 'Sidebar Tabs',
                    description: 'This is the sidebar tab list. Use these to switch the active sidebar content.',
                },
                onHighlighted: () => {
                    console.log('Tutorial opening sidebar');
                    if (mobile) {
                        window.setSidebarOpen(true);
                    }
                }
            },
            {
                element: '.icon.planet-icon-12215159187308544',
                popover: {
                    title: 'Join Chat',
                    description: 'Let\'s join the chat! Click here to enter Valour Central.',
                },
                onHighlighted: () => {
                    console.log('Tutorial closing sidebar');
                    if (mobile) {
                        window.setSidebarOpen(false);
                    }
                }
            },
        ]
    });
    startDriverObj.drive();
};
//# sourceMappingURL=tutorial.js.map