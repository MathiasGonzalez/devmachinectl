# devmachinectl
.net single file (net10+) local web for controling devmachine (e.g colima, docker etc)

## Overview


`devmachinectl` is a .NET single-file (net10+) local web application for controlling development machine tools such as Colima, Docker, Node.js, npm, and .NET SDK.

## Features

- Control Colima (start, stop, status, list)
- Control Docker (version, info, stop)
- Control Node.js (version)
- Control npm (version)
- Control .NET SDK (version, sdk check, list runtimes)


Running the application will start a local web server on `http://localhost:5050`, providing a web interface to control and monitor your development machine tools.


## Usage

To run the application, simply execute the `devmactl.cs` script using `dotnet-script`:

```bash
dotnet-script devmactl.cs
```

```bash
dotnet run devmactl.cs
```