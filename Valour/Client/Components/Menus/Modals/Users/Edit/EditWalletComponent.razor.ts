export {};

interface WalletPublicKey {
    toString(): string;
}

interface WalletProvider {
    isConnected: boolean;
    publicKey?: WalletPublicKey;
    connect(): Promise<void>;
    disconnect(): Promise<void>;
    signMessage(message: Uint8Array, encoding: string): Promise<{ signature: Uint8Array }>;
}

declare global {
    interface Window {
        solanaWeb3: {
            Connection: new (rpcUrl: string, commitment: string) => any;
            PublicKey: new (key: string) => any;
        };
        splToken: {
            u64: any;
            AccountLayout: {
                decode(data: any): { amount: any };
            };
            getMint(connection: any, mint: any): Promise<{ decimals: number }>;
        };
        solflare?: WalletProvider;
        phantom?: {
            solana?: WalletProvider;
        };
    }
}

export function init(dotnet: any) {
    return {
        async getPublicKeyFromWallet(): Promise<string | null> {
            return await connectWithWallet();
        },

        async signMessageWithWallet(nonce: string): Promise<string | null> {
            const provider = getWalletProvider();
            if (!provider) return null;

            const publicKey = await connectWithWallet();
            if (!publicKey) return null;

            try {
                const encodedMessage = new TextEncoder().encode(nonce);
                const signed = await provider.signMessage(encodedMessage, "utf8");
                return base58Encode(signed.signature);
            } catch (err) {
                console.error("Message signing cancelled or failed:", err);
                return null;
            }
        },

        async disconnectWallet(): Promise<void> {
            const provider = getWalletProvider();
            if (provider?.isConnected) {
                try {
                    await provider.disconnect();
                    console.log("Wallet disconnected");
                } catch (err) {
                    console.error("Error disconnecting wallet:", err);
                }
            }
        },

        async getTokenBalance(publicKeyBase58: string, tokenMintAddress: string): Promise<string | null> {
            try {
                const connection = new window.solanaWeb3.Connection(
                    'https://rpc.helius.xyz/?api-key=f322065c-de45-4e93-8e87-9ccd5c6e71aa',
                    'confirmed'
                );

                const publicKey = new window.solanaWeb3.PublicKey(publicKeyBase58);
                const tokenMint = new window.solanaWeb3.PublicKey(tokenMintAddress);

                const tokenAccounts = await connection.getTokenAccountsByOwner(publicKey, {
                    mint: tokenMint
                });

                if (!tokenAccounts.value.length) {
                    console.log("No token accounts found");
                    return "0";
                }

                let totalAmount = new window.splToken.u64(0);
                for (const acc of tokenAccounts.value) {
                    const accountData = window.splToken.AccountLayout.decode(acc.account.data);
                    totalAmount = totalAmount.add(accountData.amount);
                }

                const mintAccount = await window.splToken.getMint(connection, tokenMint);
                const decimals = mintAccount.decimals;
                const readableAmount = totalAmount / Math.pow(10, decimals);

                return readableAmount.toString();
            } catch (error) {
                console.error("Error getting token balance:", error);
                return null;
            }
        },

        async getWalletName(): Promise<string | null> {
            return getWalletName();
        },
        
    };
}

function getWalletProvider(): WalletProvider | null {
    if (window.solflare) {
        localStorage.setItem("wallet_provider", "Solflare");
        return window.solflare;
    }
    if (window.phantom?.solana) {
        localStorage.setItem("wallet_provider", "Phantom");
        return window.phantom.solana;
    }

    const saved = localStorage.getItem("wallet_provider");
    if (saved === "Solflare") return window.solflare ?? null;
    if (saved === "Phantom") return window.phantom?.solana ?? null;

    return null;
}

function getWalletName(): string | null {
    if (window.solflare) return "Solflare";
    if (window.phantom?.solana) return "Phantom";
    return localStorage.getItem("wallet_provider");
}

async function connectWithWallet(): Promise<string | null> {
    const provider = getWalletProvider();
    if (!provider) {
        window.open("https://chromewebstore.google.com/detail/solflare-wallet/bhhhlbepdkbapadjdnnojkbgioiodbic", "_blank");
        return null;
    }

    try {
        if (!provider.isConnected) {
            await provider.connect();
        }
        return provider.publicKey?.toString() ?? null;
    } catch (e: any) {
        if (e?.message?.toLowerCase().includes("user rejected") || e?.message?.toLowerCase().includes("cancel")) {
            console.warn("Wallet connection cancelled by user.");
        } else {
            console.error("Unexpected wallet connection error:", e);
        }
        return null;
    }
}

function base58Encode(bytes: Uint8Array): string {
    const ALPHABET = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';
    let result = '';
    let num = BigInt(0);

    for (const byte of bytes) {
        num = (num << BigInt(8)) + BigInt(byte);
    }

    while (num > 0) {
        const remainder = num % BigInt(58);
        num = num / BigInt(58);
        result = ALPHABET[Number(remainder)] + result;
    }

    for (const byte of bytes) {
        if (byte === 0) {
            result = ALPHABET[0] + result;
        } else {
            break;
        }
    }

    return result;
}
