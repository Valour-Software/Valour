![Valour logo](Valour/Client/wwwroot/favicon.ico)

# Valour

### Valour is an open-source, modern chat client which focuses on privacy and security while implementing features that bring it beyond the norm.
<br/>

## Design

Valour's messaging system is designed to keep you in control of your messages. When you send a message, it is cached for 24 hours on the Valour backend before being permanently deleted. What is kept is a hash of the message and its metadata.
<br/><br/>
This metadata allows a PSP (Peer-Server-Peer) relay subsystem to serve messages that are not cached on the Valour backend. Users within communities can decide how many cached messages they would like to store to contribute to the server message integrity, and even select entire channels and servers to archive entirely.
<br/><br/>
With the peering system allowing for extended message history, and no permanent records of any of your messages, your data is your own and we cannot sell or view your data. However, due to the hashes, we can validate that messages sent though the P2P system are valid, ensuring that no fraud can occur on the system.

## Running Valour Locally

### Minimum Requirements:

- [ASP.NET Core Runtime (With Hosting Bundle for Windows) or SDK for .NET 5](https://dotnet.microsoft.com/download/dotnet/5.0)
- [Most recent MySQL version](https://dev.mysql.com/downloads/)

### Running

Create a MySQL database using the valour.sql dump in the Server folder

First make sure in ValourConfig your DBConfig.json is has the correct information (EmailConfig.json and MSPConfig.json are most likely not going to be required)

##### Visual Studio

Run Valour.Server with IIS Express

![VS Image](https://user-images.githubusercontent.com/62479942/117558300-0e725180-b074-11eb-87bf-8eaa426950b2.png)

##### CLI

Type `dotnet run` in the Server folder

**Note:** You may have to manually update the MySQL table to verify your account when creating a new account by doing `UPDATE UserEmails SET Verified = TRUE;` when testing
