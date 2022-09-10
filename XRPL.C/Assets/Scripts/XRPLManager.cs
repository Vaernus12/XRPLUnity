using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using Xrpl.Client;
using Xrpl.Client.Models.Common;
using Xrpl.Client.Models.Methods;
using Xrpl.Client.Models.Transactions;
using Xrpl.XrplWallet;
using Ripple.Keypairs;
using Ripple.Address.Codec;
using static Ripple.Address.Codec.XrplCodec;
using static Xrpl.XrplWallet.Wallet;
using Newtonsoft.Json;

namespace XRPL.C
{
    public class XRPLManager : MonoBehaviour
    {
        public string WebSocketUrl = "wss://xrplcluster.com/"; //Main Net: 	wss://s1.ripple.com/  wss://xrplcluster.com/  Test Net: wss://s.altnet.rippletest.net/
        public string XRPLAddress = ""; //Address that holds the tokens
        public string XRPLSecret = ""; //Secret to the address. KEEP THIS PRIVATE AND SAFE!
        public string TargetAddress = ""; //Address to send tokens to for testing
        public string TargetAmount = "0.000001"; //Amount of tokens sent for testing
        public string CurrencyCode = ""; //Ticker symbol of token
        public string IssuerAddress = ""; //Address that issued the tokens
        public int AccountLinesThrottle = 10; //Number of seconds between request calls. Recommended not to change. Lower settings could result in a block from web service hosts.
        public int TxnThrottle = 1; //Number of seconds between request calls. Recommended not to change. Lower settings could result in a block from web service hosts.
        public float FeeMultiplier = 1.1f; //How many times above average fees to pay for reliable transactions
        public int MaximumFee = 11; //Maximum number of drops willing to pay for each transaction
        public float TransferFee = 0; //Usually not applicable. Leave at 0 if unsure. TransferRate of your token in %, must be between 0 and 100

        public TextMeshProUGUI PaymentText;
        private Queue<string> PaymentTextQueue;

        public TextMeshProUGUI BookOffersText;
        private Queue<string> BookOffersTextQueue;

        public TextMeshProUGUI BalanceText;
        private Queue<string> BalanceTextQueue;

        public TextMeshProUGUI TrustlinesText;
        private Queue<string> TrustlinesTextQueue;

        private Regex regex = new Regex("^[a-zA-Z][a-zA-Z1-9]*$");

        private void Start()
        {
            if (!XrplCodec.IsValidClassicAddress(XRPLAddress)) Debug.LogError("Invalid XRPL Address!");
            else Debug.Log("Validated XRPL Address!");
            if (!XrplCodec.IsValidClassicAddress(TargetAddress)) Debug.LogError("Invalid Target Address!");
            else Debug.Log("Validated Target Address!");

            string currencyCodeVal = CurrencyCode;
            if (currencyCodeVal.Length != 3) CurrencyCode = AddZeros(ConvertHex(CurrencyCode));

            PaymentTextQueue = new Queue<string>();

            BookOffersTextQueue = new Queue<string>();
            BalanceTextQueue = new Queue<string>();
            TrustlinesTextQueue = new Queue<string>();
        }

        private void Update()
        {
            if(PaymentTextQueue.Count > 0)
            {
                PaymentText.text += PaymentTextQueue.Dequeue();
            }

            if (BookOffersTextQueue.Count > 0)
            {
                BookOffersText.text += BookOffersTextQueue.Dequeue();
            }

            if (BalanceTextQueue.Count > 0)
            {
                BalanceText.text += BalanceTextQueue.Dequeue();
            }

            if (TrustlinesTextQueue.Count > 0)
            {
                TrustlinesText.text += TrustlinesTextQueue.Dequeue();
            }
        }

        public void SendPayment()
        {
            Debug.Log("Sending Payment!");
            var sendRewardTask = Task.Run(async () =>
            {
                await SendPaymentAsync();
            });
        }

        public async Task SendPaymentAsync()
        {
            try
            {
                IRippleClient client = new RippleClient(WebSocketUrl);
                client.Connect();
                uint sequence = await GetLatestAccountSequence(client, XRPLAddress);
                FeeRequest request = new FeeRequest();
                var f = await client.Fee(request);

                while (Convert.ToInt32(Math.Floor(f.Drops.OpenLedgerFee * FeeMultiplier)) > MaximumFee)
                {
                    PaymentTextQueue.Enqueue("XRPL Fees:" + " Waiting...fees too high. Current Open Ledger Fee: " + f.Drops.OpenLedgerFee + "\n");
                    PaymentTextQueue.Enqueue("XRPL Fees:" + " Fees configured based on fee multiplier: " + Convert.ToInt32(Math.Floor(f.Drops.OpenLedgerFee * FeeMultiplier)) + "\n");
                    PaymentTextQueue.Enqueue("XRPL Fees:" + " Maximum Fee Configured: " + MaximumFee + "\n");
                    Thread.Sleep(AccountLinesThrottle * 1000);
                    FeeRequest request1 = new FeeRequest();
                    f = await client.Fee(request1);
                }

                int feeInDrops = Convert.ToInt32(Math.Floor(f.Drops.OpenLedgerFee * FeeMultiplier));

                var response = await SendXRPPaymentAsync(client, sequence, feeInDrops, TransferFee);

                //Transaction Node isn't Current. Wait for Network
                if (response.EngineResult == "noCurrent" || response.EngineResult == "noNetwork")
                {
                    int retry = 0;
                    while ((response.EngineResult == "noCurrent" || response.EngineResult == "noNetwork") && retry < 3)
                    {
                        //Throttle for node to catch up
                        System.Threading.Thread.Sleep(TxnThrottle * 3000);
                        response = await SendXRPPaymentAsync(client, sequence, feeInDrops, TransferFee);
                        retry++;

                        if ((response.EngineResult == "noCurrent" || response.EngineResult == "noNetwork") && retry == 3)
                        {
                            PaymentTextQueue.Enqueue("XRP network isn't responding. Please try again later!" + "\n");
                        }
                    }
                }
                else if (response.EngineResult == "tefPAST_SEQ")
                {
                    //Get new account sequence + try again
                    sequence = await GetLatestAccountSequence(client, XRPLAddress);
                    PaymentTextQueue.Enqueue("Please try again!" + "\n");
                }
                else if (response.EngineResult == "telCAN_NOT_QUEUE_FEE")
                {
                    sequence = await GetLatestAccountSequence(client, XRPLAddress);
                    //Throttle, check fees and try again
                    System.Threading.Thread.Sleep(TxnThrottle * 3000);
                    PaymentTextQueue.Enqueue("Please try again!" + "\n");
                }
                else if (response.EngineResult == "tesSUCCESS" || response.EngineResult == "terQUEUED")
                {
                    //Transaction Accepted by node successfully.
                    PaymentTextQueue.Enqueue("Successfully sent " + TargetAmount + " " + CurrencyCode + " to " + TargetAddress + "\n");
                    sequence++;
                }
                else if (response.EngineResult == "tecPATH_DRY" || response.EngineResult == "tecDST_TAG_NEEDED")
                {
                    //Trustline was removed or Destination Tag needed for address
                    PaymentTextQueue.Enqueue("Trustline is not set!" + "\n");
                    sequence++;
                }
                else
                {
                    //Failed
                    PaymentTextQueue.Enqueue("Transaction failed!" + "\n");
                    sequence++;
                }

                client.Disconnect();
            }
            catch (Exception ex)
            {
                PaymentTextQueue.Enqueue(ex.Source + ex.Message + ex + "\n");
            }
        }

        public async Task<Submit> SendXRPPaymentAsync(IRippleClient client, uint sequence, int feeInDrops, float transferFee = 0)
        {
            try
            {
                IPayment paymentTransaction = new Payment
                {
                    Account = XRPLAddress,
                    Destination = TargetAddress,
                    Amount = new Currency { CurrencyCode = this.CurrencyCode, Issuer = IssuerAddress, Value = TargetAmount },
                    Sequence = sequence,
                    Fee = new Currency { CurrencyCode = "XRP", ValueAsNumber = feeInDrops }
                };

                if (transferFee > 0)
                {
                    paymentTransaction.SendMax = new Currency { CurrencyCode = this.CurrencyCode, Issuer = IssuerAddress, Value = (TargetAmount + (Convert.ToSingle(TargetAmount) * (transferFee / 100))).ToString() };
                }

                Wallet wallet = Wallet.FromSeed(XRPLSecret); //secret is not sent to server, offline signing only
                Dictionary<string, dynamic> paymentJson = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(paymentTransaction.ToJson());
                SignatureResult signedTx = wallet.Sign(paymentJson);

                SubmitRequest request = new SubmitRequest()
                {
                    TxBlob = signedTx.TxBlob
                };

                Submit result = await client.Submit(request);

                return result;
            }
            catch (Exception ex)
            {
                PaymentTextQueue.Enqueue(ex.Source + ex.Message + ex + "\n");
                throw new Exception(ex.Message);
            }
        }

        public async Task<uint> GetLatestAccountSequence(IRippleClient client, string account)
        {
            try
            {
                AccountInfoRequest request = new AccountInfoRequest(account);
                AccountInfo accountInfo = await client.AccountInfo(request);
                return accountInfo.AccountData.Sequence;

            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        public void GetBookOffers()
        {
            Debug.Log("Get Book Offers!");
            var getBookOffersTask = Task.Run(async () =>
            {
                await GetBookOffersAsync();
            });
        }

        public async Task GetBookOffersAsync()
        {
            try
            {
                IRippleClient client = new RippleClient(WebSocketUrl);
                client.Connect();

                TakerAmount fromCurrency = null;
                TakerAmount toCurrency = null;

                fromCurrency = new TakerAmount
                {
                    Currency = CurrencyCode,
                    Issuer = IssuerAddress
                };

                toCurrency = new TakerAmount();

                BookOffersRequest request1 = new()
                {
                    TakerGets = fromCurrency,
                    TakerPays = toCurrency
                };

                BookOffersRequest request2 = new()
                {
                    TakerGets = toCurrency,
                    TakerPays = fromCurrency
                };

                var offers = await client.BookOffers(request1);
                Thread.Sleep(TxnThrottle * 1000);
                var offers2 = await client.BookOffers(request2);

                BookOffersTextQueue.Enqueue("Asks" + "\n");

                decimal? lowestAsk = 100000;
                for (int i = offers.Offers.Count - 1; i > 0; i--)
                {
                    var value = offers.Offers[i].TakerPays.ValueAsXrp / offers.Offers[i].TakerGets.ValueAsNumber;
                    BookOffersTextQueue.Enqueue(value + "\n");
                    if (value < lowestAsk) lowestAsk = value;
                }

                BookOffersTextQueue.Enqueue("\n");

                BookOffersTextQueue.Enqueue("Bids" + "\n");

                decimal ? highestBid = 0;
                for (int i = 0; i < offers2.Offers.Count; i++)
                {
                    var value = offers2.Offers[i].TakerGets.ValueAsXrp / offers2.Offers[i].TakerPays.ValueAsNumber;
                    BookOffersTextQueue.Enqueue(value + "\n");
                    if (value > highestBid) highestBid = value;
                }

                BookOffersTextQueue.Enqueue("\n");

                var midPrice = ((lowestAsk) + (highestBid)) / 2;
                BookOffersTextQueue.Enqueue("Midprice: " + midPrice + "\n");

                var spread = lowestAsk - highestBid;
                BookOffersTextQueue.Enqueue("Spread: " + spread.ToString() + "\n");

                client.Disconnect();
            }
            catch (Exception ex)
            {
                BookOffersTextQueue.Enqueue(ex.Source + ex.Message + ex + "\n");
            }
        }

        public void ReturnAccountBalance()
        {
            var returnAccountBalanceTask = Task.Run(async () =>
            {
                await ReturnAccountBalanceAsync();
            });
        }

        public async Task ReturnAccountBalanceAsync()
        {
            try
            {
                IRippleClient client = new RippleClient(WebSocketUrl);
                client.Connect();
                
                AccountInfoRequest request = new AccountInfoRequest(TargetAddress);
                AccountInfo accountInfo = await client.AccountInfo(request);
                client.Disconnect();

                BalanceTextQueue.Enqueue((accountInfo.AccountData.Balance.ValueAsXrp.HasValue ? accountInfo.AccountData.Balance.ValueAsXrp.Value : 0) + " XRP" + "\n");
            }
            catch (Exception ex)
            {
                BalanceTextQueue.Enqueue(ex.Source + ex.Message + ex + "\n");
            }
        }

        public void ReturnTrustLines()
        {
            Debug.Log("Get Trustlines!");
            var returnTrustLinesTask = Task.Run(async () =>
            {
                await ReturnTrustLinesAsync();
            });
        }

        public async Task ReturnTrustLinesAsync(string marker = "")
        {
            try
            {
                IRippleClient client = new RippleClient(WebSocketUrl);
                client.Connect();

                AccountLinesRequest req = new AccountLinesRequest(TargetAddress);

                req.Limit = 400;
                if (marker != "")
                {
                    req.Marker = marker;
                }

                AccountLines accountLines = await client.AccountLines(req);
                if (accountLines.Marker != null)
                {
                    marker = accountLines.Marker.ToString();
                }
                else
                {
                    marker = "";
                }

                foreach (TrustLine line in accountLines.TrustLines)
                {
                    TrustlinesTextQueue.Enqueue(line.Currency + ": " + line.Balance + "\n");
                }

                client.Disconnect();
            }
            catch (Exception ex)
            {
                TrustlinesTextQueue.Enqueue(ex.Source + ex.Message + ex + "\n");
            }
        }

        public string ConvertHex(string hexString)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(hexString);
                var hexStringBytes = BitConverter.ToString(bytes);
                return hexStringBytes.Replace("-", "");
            }
            catch (Exception) { return ""; }
        }

        public string AddZeros(string s)
        {
            while (s.Length < 40)
            {
                s += "0";
            }
            return s;
        }
    }
}
