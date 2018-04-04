/*=====================================================================
 Author: David Miranda - @davemiranda
 License: MIT (http://opensource.org/licenses/mit-license.php)

 Description: KnockoutJS binding to expand and collapse elements.
 ======================================================================*/

(function (factory) {
    // Module systems magic dance.  Thanks KO validation team!

    if (typeof require === "function" && typeof exports === "object" && typeof module === "object") {
        // CommonJS or Node: hard-coded dependency on "knockout"
        factory(require("knockout"), exports);
    } else if (typeof define === "function" && define["amd"]) {
        // AMD anonymous module with hard-coded dependency on "knockout"
        define(["knockout", "exports"], factory);
    } else {
        // <script> tag: use the global 'ko' object, attaching a 'mapping' property
        factory(ko);
    }
}(function (ko) {

    function Expander(element, config, bindingContext) {
        var expander = this,
            $expander = $(element),
            $content = null,
            $toggles = null,
            $expand = null,
            $collapse = null,
            expanded = false,
            collapsedHeight = null,
            collapsedHeightPx = null;

        var defaults = {
            name: '',
            template: null,
            collapsedHeight: 0,
            animate: {
                animate: 'swing',
                duration: 400
            },
            tolerance: 50,
            accordion: ko.observable(null),
            expanded: ko.observable(false)
        };

        config = $.extend(true, defaults, config);

        if (!ko.isObservable(config.expanded)) {
            config.expanded = ko.observable(config.expanded);
        }

        var expandAnimate = $.extend({
            complete: function () {
                $content.css('max-height', 'none');
            }
        }, config.animate);

        function setExpandedStyles() {
            $expander.addClass('expanded').removeClass('collapsed');
            $toggles.addClass('expanded').removeClass('collapsed');
            $expand.hide();
            $collapse.show();
        }

        function setCollapsedStyles() {
            $expander.addClass('collapsed').removeClass('expanded');
            $toggles.addClass('collapsed').removeClass('expanded');
            $collapse.hide();
            $expand.show();
        }

        function expand() {
            setExpandedStyles();
            $content.stop(true, false).animate({
                'max-height': $content[0].scrollHeight + 'px'
            }, expandAnimate);
            config.accordion(expander);
        }

        function collapse() {
            setCollapsedStyles();
            $content.css('max-height', $content[0].scrollHeight + 'px');
            $content.stop(true, false).animate({
                'max-height': collapsedHeightPx
            }, config.animate);
        }

        var template = config.template;
        if (template) {
            var isTemplateObject = typeof template === 'object';
            var templateName = isTemplateObject ? template.name : template;
            if (typeof templateName === 'string') {
                // Transclude the innerHTML of the expander element into the requested template's data-expander-content element.
                var innerHTML = element.innerHTML;
                $expander.html($('#' + templateName).html());
                $expander.find('[data-expander-content]').eq(0).html(innerHTML);

                if (isTemplateObject) {
                    $.extend(true, bindingContext, template.data);
                }
            }
        }

        config.accordion.subscribe(function(expanded) {
            if (expanded !== null && expanded !== expander && config.expanded()) {
                config.expanded(false);
            }
        });

        config.expanded.subscribe(function(newlyExpanded) {
            if (newlyExpanded) {
                expand();
            } else {
                collapse();
            }
        });

        /**
         * Called after Knockout renders this element and its contents.  Allows for binding
         * to child elements not present until after Knockout evaluates.
         */
        expander.elementReady = function () {
            var $contents = $expander.find('[data-expander-content]');
            $content = $contents.length > 0 ? $contents.eq(0) : $expander;
            $toggles = $expander.find('[data-expander-toggles]').eq(0);
            $expand = $toggles.find('[data-expander-expand]').eq(0);
            $collapse = $toggles.find('[data-expander-collapse]').eq(0);
            var $toggle = $toggles.find('[data-expander-toggle]').eq(0);

            // calculate collapsedHeight
            if (isNaN(config.collapsedHeight)) {
                var position = $content.find(config.collapsedHeight).eq(0).position();
                collapsedHeight = position ? position.top : Number.MAX_VALUE;
            } else {
                collapsedHeight = config.collapsedHeight;
            }
            collapsedHeightPx = collapsedHeight + 'px';

            // if content needs an expander...
            if ($content.outerHeight(true) > collapsedHeight + config.tolerance) {

                // bind click events
                $expand.click(function (e) {
                    e.preventDefault();
                    config.expanded(true);
                });

                $collapse.click(function (e) {
                    e.preventDefault();
                    config.expanded(false);
                });

                $toggle.click(function (e) {
                    e.preventDefault();
                    config.expanded(!config.expanded());
                });

                // set initial CSS
                $content.css('overflow', 'hidden');

                if (config.expanded()) {
                    setExpandedStyles();
                } else {
                    setCollapsedStyles();
                    $content.css('max-height', collapsedHeightPx);
                }
            } else {
                $toggles.hide();
            }
        };

        bindingContext.expanded = config.expanded;
    }

    ko.bindingHandlers.expander = {
        init: function (element, valueAccessor, allBindings, viewModel, bindingContext) {
            var config = valueAccessor() || {};
            var expander = new Expander(element, config, bindingContext);
            setTimeout(function () {
                if (expander.elementReady) {
                    expander.elementReady();
                }
            }, 1);
        }
    };

    ko.extenders.expander = function (target, expanded) {

        var unwrapped = target();

        unwrapped.expanded = ko.observable(!!expanded);
        unwrapped.expand = function() {
            unwrapped.expanded(true);
        };
        unwrapped.collapse = function() {
            unwrapped.expanded(false);
        };
        unwrapped.toggle = function() {
            unwrapped.expanded(!unwrapped.expanded());
        };

        return target;
    };
}));