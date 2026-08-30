# Xiyi ViVeTool GUI

<p align="right">
  <a href="README.md">🇨🇳简体中文</a>
</p>

<p align="center">
  <strong>A Modern Graphical Interface for ViVeTool on Windows</strong>
</p>

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P1BW5WB82MH">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="https://get.microsoft.com/images/en-us%20dark.svg">
      <source media="(prefers-color-scheme: light)" srcset="https://get.microsoft.com/images/en-us%20light.svg">
      <img src="https://get.microsoft.com/images/en-us%20light.svg" width="220" alt="Download from the Microsoft Store">
    </picture>
  </a>
</p>

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P1BW5WB82MH">
    <img src="https://img.shields.io/badge/Microsoft%20Store-Download-0078D4?style=flat-square&logo=microsoft&logoColor=white" alt="Microsoft Store">
  </a>
  <img src="https://img.shields.io/badge/C%23-.NET-512BD4?style=flat-square&logo=dotnet&logoColor=white" alt="C# .NET">
  <img src="https://img.shields.io/badge/WinUI-Windows%20UI-0078D4?style=flat-square&logo=windows&logoColor=white" alt="WinUI">
  <img src="https://img.shields.io/badge/Windows-11-0078D4?style=flat-square&logo=windows11&logoColor=white" alt="Windows 11">
  <img src="https://img.shields.io/badge/ViVeTool-GUI-5C2D91?style=flat-square" alt="ViVeTool GUI">
</p>

---

### Introduction

Xiyi ViVeTool GUI is a modern graphical user interface for ViVeTool designed for Windows users and built with WinUI 3.

It converts ViVeTool command-line operations into an intuitive graphical interface, allowing users to view, search, and manage Windows Features without manually entering ViVeTool commands.

This project is primarily intended for Windows 11 users, Windows Insiders, and users interested in Windows hidden features and experimental Features.

### Main Features

* View Windows Features
* Search Feature IDs
* Search Feature names
* View Feature states
* Enable Features
* Disable Features
* Reset Features
* Manage Runtime State
* Manage Boot State
* Perform ViVeTool operations through a graphical interface
* Modern Windows GUI
* Lower the barrier to using ViVeTool from the command line

### Why Do We Need a GUI?

ViVeTool is traditionally operated through the command line:

vivetool /enable /id:12345678

For advanced users familiar with Windows internal Features, the command line is highly efficient. However, ordinary users need to remember command syntax and Feature IDs.

Xiyi ViVeTool GUI provides a visual way to manage Features, allowing users to browse, search, and modify Windows Features directly.

### What Is ViVeTool?

ViVeTool is a command-line tool used to configure Windows Features.

Windows contains a large number of Features controlled by Feature IDs. These Features can have different states, such as:

* Default
* Enabled
* Disabled
* Boot
* Runtime
* Experimental

ViVeTool can modify these states using Feature IDs.

### This Tool Is Suitable For

* Windows 11 users
* Windows Insider users
* Windows Feature research
* Windows experimental feature testing
* ViVeTool users
* Windows Feature ID management
* Exploring Windows hidden features
* Advanced Windows configuration

### Notes

Windows Features are closely related to the Windows Build.

The same Feature ID may behave differently, or may not exist, across different Windows versions, builds, or Insider Channels.

### Enabling experimental Features may cause:

* Unexpected behavior
* UI issues
* Changes to system component behavior
* Compatibility issues between Features
* Reduced stability on Insider Builds

Please make sure that the Feature ID is compatible with your current Windows Build before using it.

### This Project Is Not a Fork Of:

* PeterStrick/ViVeTool-GUI
* MadCkull/ViVe-3
* Other third-party ViVeTool GUI projects

### License

#### ⚠️ This software includes ViVeTool, which is licensed under GPLv3.

ViVeTool source code:

https://github.com/thebookisclosed/ViVe