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