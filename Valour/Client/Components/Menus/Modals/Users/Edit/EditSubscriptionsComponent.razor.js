// TESTING
// const clientId = 'AX0dNbrYT13_oxTH9_AkrKVvz8JOy3bgQdjeBXIl0kXFyuSZOVsh1LYDZyNS_-kc_m-z4gKBBJvywooh';
// LIVE
const clientId = 'Abz1FE0IdJu6zeioiad3EozUUnX31zjeEOl5SXJ6vck59Q_bRMffJdSG55aZHpNUFivVwDpLu3ZKuDPp';

let paypalScript = null;
let purchaseChoice = null;
let baseUrl = null;
let paypalId = null;
let token = null;
let node = null;
let dotnetRef = null;

export function setPurchaseChoice(choice) {
    purchaseChoice = choice;
}

export function setupPaypal(dn, url, id, t, n) {
    dotnetRef = dn;
    baseUrl = url;
    paypalId = id;
    token = t;
    node = n;
    
    if (!paypalScript) {
        paypalScript = document.getElementById('paypal-script');

        if (!paypalScript) {
            paypalScript = document.createElement('script');
            paypalScript.id = 'paypal-script';
            paypalScript.src = 'https://www.paypal.com/sdk/js?client-id=' + clientId + '&currency=USD';
            
            const parent = document.getElementById(id);
            parent.appendChild(paypalScript);
        }
    }
}

export function showPaypal(){
    if (document.getElementById(paypalId).children.length > 1) {
        return;
    }
    
    console.log('Showing paypal...');
    
    const FUNDING_SOURCES = [
        // EDIT FUNDING SOURCES
        paypal.FUNDING.PAYPAL,
        paypal.FUNDING.VENMO,
        paypal.FUNDING.CARD
    ];
    FUNDING_SOURCES.forEach(fundingSource => {
        paypal.Buttons({
            fundingSource,

            style: {
                layout: 'vertical',
                shape: 'rect',
                color: (fundingSource == paypal.FUNDING.PAYLATER) ? 'gold' : '',
            },

            createOrder: async (data, actions) => {
                try {
                    return await fetch(baseUrl + "api/orders/" + purchaseChoice, {
                        method: "POST",
                        headers: {
                            "Authorization": token,
                            "X-Server-Select": node,
                        }
                    })
                    .then((response) => response.json())
                    .then((order) => { 
                        console.log('ORDER:', order);
                        return order.id; 
                    });
                } catch (error) {
                    console.error(error);
                    // Handle the error or display an appropriate error message to the user
                }
            },

            onApprove: async (data, actions) => {
                try {
                    const response = await fetch(baseUrl + `api/orders/${data.orderID}/capture`, {
                        method: "POST",
                        headers: {
                            "Authorization": token,
                            "X-Server-Select": node,
                        }
                    });
                    
                    // Three cases to handle:
                    //   (1) Recoverable INSTRUMENT_DECLINED -> call actions.restart()
                    //   (2) Other non-recoverable errors -> Show a failure message
                    //   (3) Successful transaction -> Show confirmation or thank you message
                    
                    let content = await response.text();
                    
                    // ERROR
                    if (!response.ok) {
                        if (content.includes('INSTRUMENT_DECLINED')) {
                            return actions.restart();
                            // https://developer.paypal.com/docs/checkout/integration-features/funding-failure/
                        }

                        dotnetRef.invokeMethodAsync('OnPaypalFailure', content);
                    } else {
                        dotnetRef.invokeMethodAsync('OnPaypalSuccess', content);
                    }
                } catch (error) {
                    console.error(error);
                    // Handle the error or display an appropriate error message to the user
                }
            },
        }).render("#paypal-button-container");
    })
}

// Thanks to https://codepen.io/jacobgunnarsson/pen/pbPwga
export function init() {
    const Confettiful = function (el) {
        this.el = el;
        this.containerEl = null;

        this.confettiFrequency = 3;
        this.confettiColors = ['#bf06fd', '#0c06fd', '#00faff'];
        this.confettiAnimations = ['slow', 'medium', 'fast'];

        this._setupElements();
        this._renderConfetti();
    };

    Confettiful.prototype._setupElements = function () {
        const containerEl = document.createElement('div');
        const elPosition = this.el.style.position;

        if (elPosition !== 'relative' || elPosition !== 'absolute') {
            this.el.style.position = 'relative';
        }

        containerEl.classList.add('confetti-container');

        this.el.appendChild(containerEl);

        this.containerEl = containerEl;
    };

    Confettiful.prototype._renderConfetti = function () {
        this.confettiInterval = setInterval(() => {
            const confettiEl = document.createElement('div');
            const confettiSize = (Math.floor(Math.random() * 3) + 7) + 'px';
            const confettiBackground = this.confettiColors[Math.floor(Math.random() * this.confettiColors.length)];
            const confettiLeft = (Math.floor(Math.random() * this.el.offsetWidth)) + 'px';
            const confettiAnimation = this.confettiAnimations[Math.floor(Math.random() * this.confettiAnimations.length)];

            confettiEl.classList.add('confetti', 'confetti--animation-' + confettiAnimation);
            confettiEl.style.left = confettiLeft;
            confettiEl.style.width = confettiSize;
            confettiEl.style.height = confettiSize;
            confettiEl.style.backgroundColor = confettiBackground;

            confettiEl.removeTimeout = setTimeout(function () {
                confettiEl.parentNode.removeChild(confettiEl);
            }, 3000);

            this.containerEl.appendChild(confettiEl);
        }, 25);
    };

    window.confettiful = new Confettiful(document.querySelector('.top-confetti-container'));
}