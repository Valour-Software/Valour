
window.getPublicKeyFromWallet = async function () {
    if (!window.solflare) {
        window.open("https://chromewebstore.google.com/detail/solflare-wallet/bhhhlbepdkbapadjdnnojkbgioiodbic", "_blank");
        return null;
    }

    try {
        const provider = window.solflare;
        if (!provider) throw new Error("Solflare not found");

        if (!provider.isConnected) {
            await provider.connect();
            return provider.publicKey?.toString() ?? null
        }
    } catch (e) {
        console.error("Wallet connection error:", e);
        return null;
    }
};


window.signMessageWithWallet = async function (nonce) {
    if (!window.solflare) {
        console.warn("Solflare wallet is not available.");
        return null;
    }
    try {
        await window.solflare.connect();
        const encodedMessage = new TextEncoder().encode(nonce);
        const signed = await window.solflare.signMessage(encodedMessage, "utf8");
        const signatureBase58 = base58Encode(signed.signature);
        return signatureBase58;

    } catch (error) {
        console.error("Error signing message:", error);
        return null;
    }
};


window.disconnectWallet = async function () {
    if (window.solflare && window.solflare.isConnected) {
        try {
            await window.solflare.disconnect();
            console.log("Wallet disconnected");
        } catch (err) {
            console.error("Error disconnecting wallet:", err);
        }
    }
};


window.getTokenBalance = async function (publicKeyBase58, tokenMintAddress) {
    const connection = new solanaWeb3.Connection(solanaWeb3.clusterApiUrl('mainnet-beta'), 'confirmed');
    const publicKey = new solanaWeb3.PublicKey(publicKeyBase58);
    const tokenMint = new solanaWeb3.PublicKey(tokenMintAddress);

    try {
        const tokenAccounts = await connection.getTokenAccountsByOwner(publicKey, {
            mint: tokenMint
        });

        if (tokenAccounts.value.length === 0) {
            return 0;
        }

        const tokenAccountInfo = tokenAccounts.value[0];
        const accountData = solanaWeb3.AccountLayout.decode(tokenAccountInfo.account.data);
        const amount = accountData.amount;

        // Devuelve el balance (BigInt) como string
        return amount.toString();
    } catch (error) {
        console.error("Error getting token balance:", error);
        return null;
    }
};






function base58Encode(bytes) {
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

