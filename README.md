# XRPL.C Unity Samples
Samples to use with Dangell7's XRPL.C Unity asset

## Features

- Complete a payment transaction based on the given token
- Get the order books around the given token
- Get the test wallet's XRP balance
- Get the test wallet's trustline balances

## Requirements

- [Unity 2021 or greater](https://unity3d.com/get-unity/download)

## Settings

- **WebSocketURL**: Main Net: 	wss://s1.ripple.com/  wss://xrplcluster.com/  Test Net: wss://s.altnet.rippletest.net/
- **XRPLAddress**: Address that holds the tokens
- **XRPLSecret**: Secret to the address. KEEP THIS PRIVATE AND SAFE!
- **TargetAddress**: Address to send tokens to for testing
- **TargetAmount**: Amount of tokens sent for testing
- **CurrencyCode**: Ticker symbol of token
- **IssuerAddress**: //Address that issued the tokens
- **AccountLinesThrottle**: Number of seconds between request calls. Recommended not to change. Lower settings could result in a block from web service hosts
- **TxnThrottle**: Number of seconds between request calls. Recommended not to change. Lower settings could result in a block from web service hosts.
- **FeeMultiplier**: How many times above average fees to pay for reliable transactions
- **MaximumFee**: Maximum number of drops willing to pay for each transaction
- **TransferFee**: Usually not applicable. Leave at 0 if unsure. TransferRate of your token in %, must be between 0 and 100

## Getting Started

- Open the Sample Scene under Scenes folder
- Select the XRPLManager in hierarchy
- Fill out the settings within the inspector
- Run scene to test