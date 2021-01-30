var a = 10,
    b = 250,
    c = 255;

setInterval(function () {
    255 >= a && 40 == b && 40 == c ? a++ : a;
    255 == a && 40 == c && 255 >= b ? b++ : b;
    255 == a && 255 == b && 255 >= c ? c++ : c;
    255 == c && 255 == b && 10 < a ? a-- : a;
    10 == a && 255 == c && 40 < b ? b-- : b;
    10 == a && 40 == b && 40 < c ? c-- : c;

    var d = document.querySelector('.d');

    if (d != null) {
        d.style.color = 'rgb(' + a + ',' + b + ',' + c + ')'
    }
}, 5);