
// Code for resizeable main windows

function SizeEnable() {

    var man = $('#window-man');

    if (man.children().length > 1) {


            var split = Split(
                man.children(),
                {
                    minSize: [300, 300]
                }
            );
        
    }
}