// Shows every date input as yyyy-mm-dd, whatever the browser's language. A native date, month or date-time box writes
// the date the way the browser's locale does (dd/mm/yyyy, mm/dd/yyyy, ...). Here each becomes a text box that takes and shows
// ISO text, with a button that opens the browser's calendar and writes the chosen day back as ISO. The box keeps its id, name,
// value and event handlers, so forms, validation and scripts that read it are unchanged.
(function () {
    var kinds = {
        'date': { placeholder: 'yyyy-mm-dd', pattern: '\\d{4}-\\d{2}-\\d{2}', length: 10, width: '9.5rem', title: 'Use the form yyyy-mm-dd' },
        'month': { placeholder: 'yyyy-mm', pattern: '\\d{4}-\\d{2}', length: 7, width: '7rem', title: 'Use the form yyyy-mm' },
        'datetime-local': { placeholder: 'yyyy-mm-dd hh:mm', pattern: '\\d{4}-\\d{2}-\\d{2}[ T]\\d{2}:\\d{2}', length: 16, width: '12rem', title: 'Use the form yyyy-mm-dd hh:mm' }
    };

    function enhance(input) {
        var type = input.type;
        var kind = kinds[type];
        if (!kind) {
            return;
        }

        // The picker speaks the native type's own format (datetime-local has a T); the text box shows a space.
        var toBox = function (native) { return type === 'datetime-local' ? native.replace('T', ' ') : native; };
        var toNative = function (text) { return type === 'datetime-local' ? text.replace(' ', 'T') : text; };
        var pickerOptions = { min: input.min, max: input.max, step: input.step };

        input.type = 'text';
        input.value = toBox(input.value);
        input.placeholder = kind.placeholder;
        input.pattern = kind.pattern;
        input.maxLength = kind.length;
        input.title = kind.title;
        input.autocomplete = 'off';
        input.removeAttribute('min');
        input.removeAttribute('max');
        input.removeAttribute('step');
        input.style.minWidth = kind.width;

        var group = document.createElement('div');
        group.className = 'input-group' + (input.classList.contains('form-control-sm') ? ' input-group-sm' : '');
        group.style.flexWrap = 'nowrap';
        input.parentNode.insertBefore(group, input);
        group.appendChild(input);

        var picker = document.createElement('input');
        picker.type = type;
        picker.tabIndex = -1;
        picker.setAttribute('aria-hidden', 'true');
        picker.style.cssText = 'position:absolute;opacity:0;pointer-events:none;width:0;height:0;border:0;padding:0';
        if (pickerOptions.min) { picker.min = pickerOptions.min; }
        if (pickerOptions.max) { picker.max = pickerOptions.max; }

        var button = document.createElement('button');
        button.type = 'button';
        button.className = 'btn btn-outline-secondary';
        button.textContent = '\u{1F4C5}';
        button.title = 'Choose from a calendar';
        button.setAttribute('aria-label', 'Choose from a calendar');
        button.disabled = input.disabled;
        group.appendChild(button);
        group.appendChild(picker);

        button.addEventListener('click', function () {
            picker.value = new RegExp('^' + kind.pattern + '$').test(input.value) ? toNative(input.value) : '';
            if (typeof picker.showPicker === 'function') {
                picker.showPicker();
            } else {
                picker.click();
            }
        });

        picker.addEventListener('change', function () {
            input.value = toBox(picker.value);
            input.dispatchEvent(new Event('input', { bubbles: true }));
            input.dispatchEvent(new Event('change', { bubbles: true }));
        });
    }

    function enhanceAll() {
        document.querySelectorAll('input[type="date"], input[type="month"], input[type="datetime-local"]').forEach(enhance);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', enhanceAll);
    } else {
        enhanceAll();
    }
})();
