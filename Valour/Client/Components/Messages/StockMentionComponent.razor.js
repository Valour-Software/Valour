export function setupChart(id, ticker) {
    new TradingView.widget(
        {
            //"autosize": true,
            "symbol": ticker,
            "interval": "D",
            "timezone": "Etc/UTC",
            "theme": "dark",
            "style": "1",
            "locale": "en",
            "enable_publishing": false,
            "allow_symbol_change": true,
            "container_id": id,
            "width": "100%",
            "height": "400px"
        }
    );
}

export function setupSummary(id, ticker) {
    const script = document.createElement('script');
    script.attributes['async'] = true
    script.src = `https://s3.tradingview.com/external-embedding/embed-widget-single-quote.js`
    script.innerHTML = `
        {
          "symbol": "${ticker}",
          "width": "100%",
          "colorTheme": "dark",
          "isTransparent": false,
          "locale": "en"
        }
    `;
    
    document.getElementById(id).appendChild(script);
}