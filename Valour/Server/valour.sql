-- phpMyAdmin SQL Dump
-- version 4.9.5deb2
-- https://www.phpmyadmin.net/
--
-- Host: localhost:3306
-- Generation Time: Apr 20, 2021 at 04:24 AM
-- Server version: 8.0.23-0ubuntu0.20.04.1
-- PHP Version: 7.4.3

SET SQL_MODE = "NO_AUTO_VALUE_ON_ZERO";
SET AUTOCOMMIT = 0;
START TRANSACTION;
SET time_zone = "+00:00";


/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;

--
-- Database: `valour`
--

-- --------------------------------------------------------

--
-- Table structure for table `AuthTokens`
--

CREATE TABLE `AuthTokens` (
  `Id` varchar(36) NOT NULL,
  `App_Id` varchar(36) NOT NULL,
  `User_Id` bigint UNSIGNED NOT NULL,
  `Scope` bigint UNSIGNED NOT NULL,
  `Time` datetime NOT NULL,
  `Expires` datetime NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- --------------------------------------------------------

--
-- Table structure for table `CategoryPermissionsNodes`
--

CREATE TABLE `CategoryPermissionsNodes` (
  `Id` bigint UNSIGNED NOT NULL,
  `Code` bigint UNSIGNED NOT NULL,
  `Code_Mask` bigint UNSIGNED NOT NULL,
  `Category_Id` bigint UNSIGNED NOT NULL,
  `Planet_Id` bigint UNSIGNED NOT NULL,
  `Role_Id` bigint UNSIGNED NOT NULL,
  `ChatChannel_Code` bigint UNSIGNED NOT NULL,
  `ChatChannel_Code_Mask` bigint UNSIGNED NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- --------------------------------------------------------

--
-- Table structure for table `ChatChannelPermissionsNodes`
--

CREATE TABLE `ChatChannelPermissionsNodes` (
  `Id` bigint UNSIGNED NOT NULL,
  `Code` bigint UNSIGNED NOT NULL,
  `Code_Mask` bigint UNSIGNED NOT NULL,
  `Channel_Id` bigint UNSIGNED NOT NULL,
  `Planet_Id` bigint UNSIGNED NOT NULL,
  `Role_Id` bigint UNSIGNED NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- --------------------------------------------------------

--
-- Table structure for table `Credentials`
--

CREATE TABLE `Credentials` (
  `Id` bigint UNSIGNED NOT NULL,
  `User_Id` bigint UNSIGNED NOT NULL,
  `Credential_Type` varchar(16) NOT NULL,
  `Identifier` varchar(64) NOT NULL,
  `Secret` varbinary(32) NOT NULL,
  `Salt` varbinary(32) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- --------------------------------------------------------

--
-- Table structure for table `EmailConfirmCodes`
--

CREATE TABLE `EmailConfirmCodes` (
  `Code` varchar(36) NOT NULL,
  `User_Id` bigint UNSIGNED NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- --------------------------------------------------------

--
-- Table structure for table `PasswordRecoveries`
--

CREATE TABLE `PasswordRecoveries` (
  `Code` varchar(36) NOT NULL,
  `User_Id` bigint UNSIGNED NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- --------------------------------------------------------

--
-- Table structure for table `PlanetBans`
--

CREATE TABLE `PlanetBans` (
  `Id` bigint UNSIGNED NOT NULL,
  `User_Id` bigint UNSIGNED NOT NULL,
  `Planet_Id` bigint UNSIGNED NOT NULL,
  `Banner_Id` bigint UNSIGNED NOT NULL,
  `Reason` tinytext NOT NULL,
  `Time` datetime NOT NULL,
  `Minutes` int UNSIGNED NOT NULL,
  `Permanent` tinyint(1) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- --------------------------------------------------------

--
-- Table structure for table `PlanetCategories`
--

CREATE TABLE `PlanetCategories` (
  `Id` bigint UNSIGNED NOT NULL,
  `Name` varchar(32) NOT NULL,
  `Planet_Id` bigint UNSIGNED NOT NULL,
  `Parent_Id` bigint UNSIGNED DEFAULT NULL,
  `Position` smallint UNSIGNED NOT NULL,
  `Description` tinytext NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- --------------------------------------------------------

--
-- Table structure for table `PlanetChatChannels`
--

CREATE TABLE `PlanetChatChannels` (
  `Id` bigint UNSIGNED NOT NULL,
  `Name` varchar(32) NOT NULL,
  `Planet_Id` bigint UNSIGNED NOT NULL,
  `Message_Count` bigint UNSIGNED NOT NULL,
  `Parent_Id` bigint UNSIGNED DEFAULT NULL,
  `Position` smallint UNSIGNED NOT NULL,
  `Description` tinytext NOT NULL,
  `Inherits_Perms` tinyint(1) NOT NULL DEFAULT '1'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- --------------------------------------------------------

--
-- Table structure for table `PlanetInvites`
--

CREATE TABLE `PlanetInvites` (
  `Id` bigint UNSIGNED NOT NULL,
  `Code` varchar(8) NOT NULL,
  `Planet_Id` bigint UNSIGNED NOT NULL,
  `Issuer_Id` bigint UNSIGNED NOT NULL,
  `Time` datetime NOT NULL,
  `Hours` int UNSIGNED DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- --------------------------------------------------------

--
-- Table structure for table `PlanetMembers`
--

CREATE TABLE `PlanetMembers` (
  `Id` bigint UNSIGNED NOT NULL,
  `User_Id` bigint UNSIGNED NOT NULL,
  `Planet_Id` bigint UNSIGNED NOT NULL,
  `Nickname` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `Member_Pfp` tinytext
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- --------------------------------------------------------

--
-- Table structure for table `PlanetMessages`
--

CREATE TABLE `PlanetMessages` (
  `Id` bigint UNSIGNED NOT NULL,
  `Author_Id` bigint UNSIGNED NOT NULL,
  `Content` text NOT NULL,
  `TimeSent` datetime NOT NULL,
  `Channel_Id` bigint UNSIGNED NOT NULL,
  `Message_Index` bigint UNSIGNED NOT NULL,
  `Planet_Id` bigint UNSIGNED NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- --------------------------------------------------------

--
-- Table structure for table `PlanetRoleMembers`
--

CREATE TABLE `PlanetRoleMembers` (
  `Id` bigint UNSIGNED NOT NULL,
  `User_Id` bigint UNSIGNED NOT NULL,
  `Role_Id` bigint UNSIGNED NOT NULL,
  `Planet_Id` bigint UNSIGNED NOT NULL,
  `Member_Id` bigint UNSIGNED NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- --------------------------------------------------------

--
-- Table structure for table `PlanetRoles`
--

CREATE TABLE `PlanetRoles` (
  `Id` bigint UNSIGNED NOT NULL,
  `Name` varchar(32) NOT NULL,
  `Position` int UNSIGNED NOT NULL,
  `Planet_Id` bigint UNSIGNED NOT NULL,
  `Color_Red` tinyint UNSIGNED NOT NULL,
  `Color_Green` tinyint UNSIGNED NOT NULL,
  `Color_Blue` tinyint UNSIGNED NOT NULL,
  `Bold` tinyint(1) NOT NULL,
  `Italics` tinyint(1) NOT NULL,
  `Permissions` bigint UNSIGNED NOT NULL DEFAULT '0'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- --------------------------------------------------------

--
-- Table structure for table `Planets`
--

CREATE TABLE `Planets` (
  `Id` bigint UNSIGNED NOT NULL,
  `Owner_Id` bigint UNSIGNED NOT NULL,
  `Name` varchar(32) NOT NULL,
  `Image_Url` text NOT NULL,
  `Description` text NOT NULL,
  `Public` tinyint(1) NOT NULL,
  `Member_Count` int UNSIGNED NOT NULL,
  `Default_Role_Id` bigint UNSIGNED NOT NULL,
  `Main_Channel_Id` bigint UNSIGNED NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- --------------------------------------------------------

--
-- Table structure for table `Referrals`
--

CREATE TABLE `Referrals` (
  `Id` bigint UNSIGNED NOT NULL,
  `User_Id` bigint UNSIGNED NOT NULL,
  `Referrer_Id` bigint UNSIGNED NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- --------------------------------------------------------

--
-- Table structure for table `ServerMessages`
--

CREATE TABLE `ServerMessages` (
  `Hash` varbinary(32) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- --------------------------------------------------------

--
-- Table structure for table `Stats`
--

CREATE TABLE `Stats` (
  `Id` bigint UNSIGNED NOT NULL,
  `Time` datetime NOT NULL,
  `MessagesSent` bigint NOT NULL,
  `UserCount` bigint NOT NULL,
  `PlanetCount` bigint NOT NULL,
  `PlanetMemberCount` bigint NOT NULL,
  `ChannelCount` bigint NOT NULL,
  `CategoryCount` bigint DEFAULT NULL,
  `Message24hCount` bigint DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- --------------------------------------------------------

--
-- Table structure for table `UserEmails`
--

CREATE TABLE `UserEmails` (
  `Email` varchar(128) NOT NULL,
  `Verified` tinyint(1) NOT NULL,
  `User_Id` bigint UNSIGNED NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- --------------------------------------------------------

--
-- Table structure for table `Users`
--

CREATE TABLE `Users` (
  `Id` bigint UNSIGNED NOT NULL,
  `Username` varchar(32) NOT NULL,
  `Join_DateTime` datetime NOT NULL,
  `Pfp_Url` text,
  `Bot` tinyint(1) NOT NULL DEFAULT '0',
  `Disabled` tinyint(1) NOT NULL,
  `Valour_Staff` tinyint(1) NOT NULL DEFAULT '0',
  `UserState_Value` int NOT NULL DEFAULT '0',
  `Last_Active` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

--
-- Indexes for dumped tables
--

--
-- Indexes for table `AuthTokens`
--
ALTER TABLE `AuthTokens`
  ADD PRIMARY KEY (`Id`),
  ADD KEY `User_Id` (`User_Id`);

--
-- Indexes for table `CategoryPermissionsNodes`
--
ALTER TABLE `CategoryPermissionsNodes`
  ADD PRIMARY KEY (`Id`),
  ADD KEY `Channel_Id` (`Category_Id`),
  ADD KEY `Planet_Id` (`Planet_Id`),
  ADD KEY `Role_Id` (`Role_Id`);

--
-- Indexes for table `ChatChannelPermissionsNodes`
--
ALTER TABLE `ChatChannelPermissionsNodes`
  ADD PRIMARY KEY (`Id`),
  ADD KEY `Channel_Id` (`Channel_Id`),
  ADD KEY `Planet_Id` (`Planet_Id`),
  ADD KEY `Role_Id` (`Role_Id`);

--
-- Indexes for table `Credentials`
--
ALTER TABLE `Credentials`
  ADD PRIMARY KEY (`Id`),
  ADD UNIQUE KEY `Salt` (`Salt`),
  ADD KEY `User_Id` (`User_Id`);

--
-- Indexes for table `EmailConfirmCodes`
--
ALTER TABLE `EmailConfirmCodes`
  ADD PRIMARY KEY (`Code`),
  ADD KEY `User_Id` (`User_Id`);

--
-- Indexes for table `PasswordRecoveries`
--
ALTER TABLE `PasswordRecoveries`
  ADD PRIMARY KEY (`Code`),
  ADD KEY `User_Id` (`User_Id`);

--
-- Indexes for table `PlanetBans`
--
ALTER TABLE `PlanetBans`
  ADD PRIMARY KEY (`Id`),
  ADD KEY `User_Id` (`User_Id`),
  ADD KEY `Planet_Id` (`Planet_Id`),
  ADD KEY `Banner_Id` (`Banner_Id`);

--
-- Indexes for table `PlanetCategories`
--
ALTER TABLE `PlanetCategories`
  ADD PRIMARY KEY (`Id`),
  ADD KEY `Planet_Id` (`Planet_Id`),
  ADD KEY `Category_Id` (`Parent_Id`);

--
-- Indexes for table `PlanetChatChannels`
--
ALTER TABLE `PlanetChatChannels`
  ADD PRIMARY KEY (`Id`),
  ADD KEY `Planet_Id` (`Planet_Id`),
  ADD KEY `Category_Id` (`Parent_Id`);

--
-- Indexes for table `PlanetInvites`
--
ALTER TABLE `PlanetInvites`
  ADD PRIMARY KEY (`Id`),
  ADD KEY `Planet_Id` (`Planet_Id`),
  ADD KEY `Issuer_Id` (`Issuer_Id`);

--
-- Indexes for table `PlanetMembers`
--
ALTER TABLE `PlanetMembers`
  ADD PRIMARY KEY (`Id`),
  ADD KEY `User_Id` (`User_Id`),
  ADD KEY `Planet_Id` (`Planet_Id`);

--
-- Indexes for table `PlanetMessages`
--
ALTER TABLE `PlanetMessages`
  ADD PRIMARY KEY (`Id`),
  ADD KEY `Author_Id` (`Author_Id`),
  ADD KEY `Channel_Id` (`Channel_Id`),
  ADD KEY `Planet_Id` (`Planet_Id`);

--
-- Indexes for table `PlanetRoleMembers`
--
ALTER TABLE `PlanetRoleMembers`
  ADD PRIMARY KEY (`Id`),
  ADD KEY `User_Id` (`User_Id`),
  ADD KEY `Role_Id` (`Role_Id`),
  ADD KEY `Planet_Id` (`Planet_Id`),
  ADD KEY `Member_Id` (`Member_Id`);

--
-- Indexes for table `PlanetRoles`
--
ALTER TABLE `PlanetRoles`
  ADD PRIMARY KEY (`Id`),
  ADD KEY `Planet_Id` (`Planet_Id`);

--
-- Indexes for table `Planets`
--
ALTER TABLE `Planets`
  ADD PRIMARY KEY (`Id`),
  ADD KEY `Owner_Id` (`Owner_Id`),
  ADD KEY `Default_Role_Id` (`Default_Role_Id`),
  ADD KEY `Main_Channel_Id` (`Main_Channel_Id`);

--
-- Indexes for table `Referrals`
--
ALTER TABLE `Referrals`
  ADD PRIMARY KEY (`Id`),
  ADD KEY `User_Id` (`User_Id`),
  ADD KEY `Referrer_Id` (`Referrer_Id`);

--
-- Indexes for table `ServerMessages`
--
ALTER TABLE `ServerMessages`
  ADD PRIMARY KEY (`Hash`);

--
-- Indexes for table `Stats`
--
ALTER TABLE `Stats`
  ADD PRIMARY KEY (`Id`);

--
-- Indexes for table `UserEmails`
--
ALTER TABLE `UserEmails`
  ADD PRIMARY KEY (`Email`),
  ADD KEY `User_Id` (`User_Id`);

--
-- Indexes for table `Users`
--
ALTER TABLE `Users`
  ADD PRIMARY KEY (`Id`),
  ADD UNIQUE KEY `Username` (`Username`),
  ADD UNIQUE KEY `Id` (`Id`),
  ADD KEY `Id_2` (`Id`);

--
-- AUTO_INCREMENT for dumped tables
--

--
-- AUTO_INCREMENT for table `CategoryPermissionsNodes`
--
ALTER TABLE `CategoryPermissionsNodes`
  MODIFY `Id` bigint UNSIGNED NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `ChatChannelPermissionsNodes`
--
ALTER TABLE `ChatChannelPermissionsNodes`
  MODIFY `Id` bigint UNSIGNED NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `Credentials`
--
ALTER TABLE `Credentials`
  MODIFY `Id` bigint UNSIGNED NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `PlanetMessages`
--
ALTER TABLE `PlanetMessages`
  MODIFY `Id` bigint UNSIGNED NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `Referrals`
--
ALTER TABLE `Referrals`
  MODIFY `Id` bigint UNSIGNED NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `Stats`
--
ALTER TABLE `Stats`
  MODIFY `Id` bigint UNSIGNED NOT NULL AUTO_INCREMENT;

--
-- Constraints for dumped tables
--

--
-- Constraints for table `AuthTokens`
--
ALTER TABLE `AuthTokens`
  ADD CONSTRAINT `AuthTokens_ibfk_1` FOREIGN KEY (`User_Id`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE;

--
-- Constraints for table `ChatChannelPermissionsNodes`
--
ALTER TABLE `ChatChannelPermissionsNodes`
  ADD CONSTRAINT `ChatChannelPermissionsNodes_ibfk_1` FOREIGN KEY (`Planet_Id`) REFERENCES `Planets` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE,
  ADD CONSTRAINT `ChatChannelPermissionsNodes_ibfk_2` FOREIGN KEY (`Role_Id`) REFERENCES `PlanetRoles` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE,
  ADD CONSTRAINT `ChatChannelPermissionsNodes_ibfk_3` FOREIGN KEY (`Channel_Id`) REFERENCES `PlanetChatChannels` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE;

--
-- Constraints for table `Credentials`
--
ALTER TABLE `Credentials`
  ADD CONSTRAINT `Credentials_ibfk_1` FOREIGN KEY (`User_Id`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE;

--
-- Constraints for table `EmailConfirmCodes`
--
ALTER TABLE `EmailConfirmCodes`
  ADD CONSTRAINT `EmailConfirmCodes_ibfk_1` FOREIGN KEY (`User_Id`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE;

--
-- Constraints for table `PlanetBans`
--
ALTER TABLE `PlanetBans`
  ADD CONSTRAINT `PlanetBans_ibfk_1` FOREIGN KEY (`User_Id`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE,
  ADD CONSTRAINT `PlanetBans_ibfk_2` FOREIGN KEY (`Banner_Id`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE,
  ADD CONSTRAINT `PlanetBans_ibfk_3` FOREIGN KEY (`Planet_Id`) REFERENCES `Planets` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE;

--
-- Constraints for table `PlanetCategories`
--
ALTER TABLE `PlanetCategories`
  ADD CONSTRAINT `PlanetCategories_ibfk_1` FOREIGN KEY (`Planet_Id`) REFERENCES `Planets` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE;

--
-- Constraints for table `PlanetChatChannels`
--
ALTER TABLE `PlanetChatChannels`
  ADD CONSTRAINT `PlanetChatChannels_ibfk_1` FOREIGN KEY (`Planet_Id`) REFERENCES `Planets` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE;

--
-- Constraints for table `PlanetInvites`
--
ALTER TABLE `PlanetInvites`
  ADD CONSTRAINT `PlanetInvites_ibfk_1` FOREIGN KEY (`Planet_Id`) REFERENCES `Planets` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE,
  ADD CONSTRAINT `PlanetInvites_ibfk_2` FOREIGN KEY (`Issuer_Id`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE;

--
-- Constraints for table `PlanetMembers`
--
ALTER TABLE `PlanetMembers`
  ADD CONSTRAINT `PlanetMembers_ibfk_1` FOREIGN KEY (`User_Id`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE,
  ADD CONSTRAINT `PlanetMembers_ibfk_2` FOREIGN KEY (`Planet_Id`) REFERENCES `Planets` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE;

--
-- Constraints for table `PlanetMessages`
--
ALTER TABLE `PlanetMessages`
  ADD CONSTRAINT `PlanetMessages_ibfk_1` FOREIGN KEY (`Author_Id`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE,
  ADD CONSTRAINT `PlanetMessages_ibfk_2` FOREIGN KEY (`Channel_Id`) REFERENCES `PlanetChatChannels` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE,
  ADD CONSTRAINT `PlanetMessages_ibfk_3` FOREIGN KEY (`Planet_Id`) REFERENCES `Planets` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE;

--
-- Constraints for table `PlanetRoleMembers`
--
ALTER TABLE `PlanetRoleMembers`
  ADD CONSTRAINT `PlanetRoleMembers_ibfk_1` FOREIGN KEY (`User_Id`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE,
  ADD CONSTRAINT `PlanetRoleMembers_ibfk_2` FOREIGN KEY (`Planet_Id`) REFERENCES `Planets` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE,
  ADD CONSTRAINT `PlanetRoleMembers_ibfk_3` FOREIGN KEY (`Role_Id`) REFERENCES `PlanetRoles` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE,
  ADD CONSTRAINT `PlanetRoleMembers_ibfk_4` FOREIGN KEY (`Member_Id`) REFERENCES `PlanetMembers` (`Id`) ON UPDATE CASCADE;

--
-- Constraints for table `PlanetRoles`
--
ALTER TABLE `PlanetRoles`
  ADD CONSTRAINT `PlanetRoles_ibfk_1` FOREIGN KEY (`Planet_Id`) REFERENCES `Planets` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE;

--
-- Constraints for table `Planets`
--
ALTER TABLE `Planets`
  ADD CONSTRAINT `Planets_ibfk_1` FOREIGN KEY (`Owner_Id`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE;

--
-- Constraints for table `Referrals`
--
ALTER TABLE `Referrals`
  ADD CONSTRAINT `Referrals_ibfk_1` FOREIGN KEY (`User_Id`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE,
  ADD CONSTRAINT `Referrals_ibfk_2` FOREIGN KEY (`Referrer_Id`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE;

--
-- Constraints for table `UserEmails`
--
ALTER TABLE `UserEmails`
  ADD CONSTRAINT `UserEmails_ibfk_1` FOREIGN KEY (`User_Id`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT ON UPDATE CASCADE;
COMMIT;

/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
