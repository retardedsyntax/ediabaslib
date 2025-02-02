﻿using System;
using System.Collections.Generic;
using System.Threading;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using AndroidX.AppCompat.App;

namespace BmwDeepObd
{
    [Android.App.Activity(Label = "@string/trans_api_key_title",
        Name = ActivityCommon.AppNameSpace + "." + nameof(TranslateKeyActivity),
        WindowSoftInputMode = SoftInput.StateAlwaysHidden,
        ConfigurationChanges = ActivityConfigChanges)]
    public class TranslateKeyActivity : BaseActivity, View.IOnTouchListener
    {
        public class InstanceData
        {
            public InstanceData()
            {
                OldTranslator = string.Empty;
                OldYandexApiKey = string.Empty;
                OldIbmTranslatorApiKey = string.Empty;
                OldIbmTranslatorUrl = string.Empty;
                OldDeeplApiKey = string.Empty;
            }

            public string OldTranslator { get; set; }
            public string OldYandexApiKey { get; set; }
            public string OldIbmTranslatorApiKey { get; set; }
            public string OldIbmTranslatorUrl { get; set; }
            public string OldDeeplApiKey { get; set; }
        }

        private InstanceData _instanceData = new InstanceData();
        private bool _activityRecreated;
        private InputMethodManager _imm;
        private Timer _clipboardCheckTimer;
        private ActivityCommon _activityCommon;
        private View _contentView;
        private TextView _textViewCaptionTranslator;
        private RadioButton _radioButtonTranslatorYandex;
        private RadioButton _radioButtonTranslatorIbm;
        private RadioButton _radioButtonTranslatorDeepl;
        private TextView _textViewTransKeyDesc;
        private LinearLayout _layoutYandexKey;
        private Button _buttonYandexApiKeyCreate;
        private Button _buttonYandexApiKeyGet;
        private Button _buttonYandexApiKeyPaste;
        private EditText _editTextYandexApiKey;
        private TextView _textViewApiUrlPasteTitle;
        private Button _buttonApiUrlPaste;
        private EditText _editTextApiUrl;
        private Button _buttonYandexApiKeyTest;
        private TextView _textViewYandexApiKeyTestResult;
        private bool _ignoreChange;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            SetTheme(ActivityCommon.SelectedThemeId);
            base.OnCreate(savedInstanceState);
            _allowTitleHiding = false;

            if (savedInstanceState != null)
            {
                _activityRecreated = true;
                _instanceData = GetInstanceState(savedInstanceState, _instanceData) as InstanceData;
            }

            SupportActionBar.SetHomeButtonEnabled(true);
            SupportActionBar.SetDisplayShowHomeEnabled(true);
            SupportActionBar.SetDisplayHomeAsUpEnabled(true);
            SetContentView(Resource.Layout.translate_key_select);

            _imm = (InputMethodManager)GetSystemService(InputMethodService);
            _contentView = FindViewById<View>(Android.Resource.Id.Content);

            SetResult(Android.App.Result.Canceled);

            if (!_activityRecreated)
            {
                _instanceData.OldTranslator = ActivityCommon.SelectedTranslator.ToString();
                _instanceData.OldYandexApiKey = ActivityCommon.YandexApiKey ?? string.Empty;
                _instanceData.OldIbmTranslatorApiKey = ActivityCommon.IbmTranslatorApiKey ?? string.Empty;
                _instanceData.OldIbmTranslatorUrl = ActivityCommon.IbmTranslatorUrl ?? string.Empty;
                _instanceData.OldDeeplApiKey = ActivityCommon.DeeplApiKey ?? string.Empty;
            }

            _activityCommon = new ActivityCommon(this);

            _textViewCaptionTranslator = FindViewById<TextView>(Resource.Id.textViewCaptionTranslator);
            _radioButtonTranslatorYandex = FindViewById<RadioButton>(Resource.Id.radioButtonTranslatorYandex);
            _radioButtonTranslatorYandex.CheckedChange += (sender, e) =>
            {
                if (_ignoreChange)
                {
                    return;
                }

                UpdateTranslatorType();
                UpdateDisplay();
            };

            _radioButtonTranslatorIbm = FindViewById<RadioButton>(Resource.Id.radioButtonTranslatorIbm);
            _radioButtonTranslatorIbm.CheckedChange += (sender, e) =>
            {
                if (_ignoreChange)
                {
                    return;
                }

                UpdateTranslatorType();
                UpdateDisplay();
            };

            _radioButtonTranslatorDeepl = FindViewById<RadioButton>(Resource.Id.radioButtonTranslatorDeepl);
            _radioButtonTranslatorDeepl.CheckedChange += (sender, e) =>
            {
                if (_ignoreChange)
                {
                    return;
                }

                UpdateTranslatorType();
                UpdateDisplay();
            };

            _textViewTransKeyDesc = FindViewById<TextView>(Resource.Id.textViewTransKeyDesc);

            _layoutYandexKey = FindViewById<LinearLayout>(Resource.Id.layoutYandexKey);
            _layoutYandexKey.SetOnTouchListener(this);

            _editTextYandexApiKey = FindViewById<EditText>(Resource.Id.editTextYandexApiKey);

            _textViewApiUrlPasteTitle = FindViewById<TextView>(Resource.Id.textViewApiUrlPasteTitle);

            _editTextApiUrl = FindViewById<EditText>(Resource.Id.editTextApiUrl);

            _buttonYandexApiKeyCreate = FindViewById<Button>(Resource.Id.buttonYandexKeyCreate);
            _buttonYandexApiKeyCreate.SetOnTouchListener(this);
            _buttonYandexApiKeyCreate.Click += (sender, args) =>
            {
                if (_activityCommon == null)
                {
                    return;
                }

                _activityCommon.ShowWifiConnectedWarning(() =>
                {
                    if (_activityCommon == null)
                    {
                        return;
                    }

                    switch (ActivityCommon.SelectedTranslator)
                    {
                        case ActivityCommon.TranslatorType.YandexTranslate:
                            _activityCommon.OpenWebUrl("https://translate.yandex.com/developers/keys");
                            break;

                        case ActivityCommon.TranslatorType.IbmWatson:
                            _activityCommon.OpenWebUrl("https://cloud.ibm.com/catalog/services/language-translator");
                            break;

                        case ActivityCommon.TranslatorType.Deepl:
                            _activityCommon.OpenWebUrl("https://www.deepl.com/account");
                            break;
                    }
                });
            };

            _buttonYandexApiKeyGet = FindViewById<Button>(Resource.Id.buttonYandexKeyGet);
            _buttonYandexApiKeyGet.SetOnTouchListener(this);
            _buttonYandexApiKeyGet.Click += (sender, args) =>
            {
                if (_activityCommon == null)
                {
                    return;
                }

                _activityCommon.ShowWifiConnectedWarning(() =>
                {
                    if (_activityCommon == null)
                    {
                        return;
                    }

                    switch (ActivityCommon.SelectedTranslator)
                    {
                        case ActivityCommon.TranslatorType.YandexTranslate:
                            _activityCommon.OpenWebUrl("https://translate.yandex.com/developers/keys");
                            break;

                        case ActivityCommon.TranslatorType.IbmWatson:
                            _activityCommon.OpenWebUrl("https://cloud.ibm.com/resources");
                            break;

                        case ActivityCommon.TranslatorType.Deepl:
                            _activityCommon.OpenWebUrl("https://www.deepl.com/de/account/summary");
                            break;
                    }
                });
            };

            _buttonYandexApiKeyPaste = FindViewById<Button>(Resource.Id.buttonYandexKeyPaste);
            _buttonYandexApiKeyPaste.SetOnTouchListener(this);
            _buttonYandexApiKeyPaste.Click += (sender, args) =>
            {
                if (_activityCommon == null)
                {
                    return;
                }

                string clipText = _activityCommon.GetClipboardText();
                if (!string.IsNullOrWhiteSpace(clipText))
                {
                    try
                    {
                        _ignoreChange = true;
                        _editTextYandexApiKey.Text = clipText.Trim();
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                    finally
                    {
                        _ignoreChange = false;
                    }

                    UpdateButtonState();
                }
            };
            _buttonYandexApiKeyPaste.TextChanged += (sender, args) =>
            {
                if (_ignoreChange)
                {
                    return;
                }

                UpdateButtonState();
            };

            _buttonApiUrlPaste = FindViewById<Button>(Resource.Id.buttonApiUrlPaste);
            _buttonApiUrlPaste.SetOnTouchListener(this);
            _buttonApiUrlPaste.Click += (sender, args) =>
            {
                if (_activityCommon == null)
                {
                    return;
                }

                string clipText = _activityCommon.GetClipboardText();
                if (!string.IsNullOrWhiteSpace(clipText))
                {
                    try
                    {
                        _ignoreChange = true;
                        _editTextApiUrl.Text = clipText.Trim();
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                    finally
                    {
                        _ignoreChange = false;
                    }

                    UpdateButtonState();
                }
            };
            _buttonApiUrlPaste.TextChanged += (sender, args) =>
            {
                if (_ignoreChange)
                {
                    return;
                }

                UpdateButtonState();
            };

            _textViewYandexApiKeyTestResult = FindViewById<TextView>(Resource.Id.textViewYandexKeyTestResult);

            _buttonYandexApiKeyTest = FindViewById<Button>(Resource.Id.buttonYandexKeyTest);
            _buttonYandexApiKeyTest.SetOnTouchListener(this);
            _buttonYandexApiKeyTest.Click += (sender, args) =>
            {
                if (_activityCommon == null)
                {
                    return;
                }

                UpdateSettings();
                _textViewYandexApiKeyTestResult.Text = string.Empty;

                if (!_activityCommon.TranslateStrings(new List<string> {"Dieser Text wurde erfolgreich \x00fcbersetzt"}, list =>
                {
                    if (_activityCommon == null)
                    {
                        return;
                    }
                    if (list != null && list.Count > 0)
                    {
                        _textViewYandexApiKeyTestResult.Text = list[0];
                    }
                    else
                    {
                        _textViewYandexApiKeyTestResult.Text = GetString(Resource.String.button_api_key_test_failed);
                    }
                }, true))
                {
                    _textViewYandexApiKeyTestResult.Text = GetString(Resource.String.button_api_key_test_failed);
                }
            };

            UpdateDisplay();
        }

        protected override void OnSaveInstanceState(Bundle outState)
        {
            StoreInstanceState(outState, _instanceData);
            base.OnSaveInstanceState(outState);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            DisposeTimer();
            _activityCommon?.Dispose();
            _activityCommon = null;
        }

        protected override void OnResume()
        {
            base.OnResume();
            UpdateButtonState();
            if (_clipboardCheckTimer == null)
            {
                _clipboardCheckTimer = new Timer(state =>
                {
                    RunOnUiThread(() =>
                    {
                        if (_activityCommon == null)
                        {
                            return;
                        }
                        UpdateButtonState();
                    });
                }, null, 1000, 1000);
            }
        }

        protected override void OnPause()
        {
            base.OnPause();
            DisposeTimer();
        }

        public override void OnBackPressedEvent()
        {
            if (StoreYandexKey((sender, args) =>
            {
                if (_activityCommon == null)
                {
                    return;
                }
                base.OnBackPressedEvent();
            }))
            {
                base.OnBackPressedEvent();
            }
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            HideKeyboard();
            switch (item.ItemId)
            {
                case Android.Resource.Id.Home:
                    if (StoreYandexKey((sender, args) =>
                    {
                        if (_activityCommon == null)
                        {
                            return;
                        }
                        Finish();
                    }))
                    {
                        Finish();
                    }
                    return true;
            }
            return base.OnOptionsItemSelected(item);
        }

        public bool OnTouch(View v, MotionEvent e)
        {
            switch (e.Action)
            {
                case MotionEventActions.Down:
                    HideKeyboard();
                    break;
            }
            return false;
        }

        private void DisposeTimer()
        {
            if (_clipboardCheckTimer != null)
            {
                _clipboardCheckTimer.Dispose();
                _clipboardCheckTimer = null;
            }
        }

        private void UpdateButtonState()
        {
            if (_activityCommon == null)
            {
                return;
            }

            try
            {
                _ignoreChange = true;
                _textViewTransKeyDesc.Text = string.Format(GetString(Resource.String.trans_key_desc), _activityCommon.TranslatorName());

                switch (ActivityCommon.SelectedTranslator)
                {
                    case ActivityCommon.TranslatorType.IbmWatson:
                        _radioButtonTranslatorIbm.Checked = true;
                        break;

                    case ActivityCommon.TranslatorType.Deepl:
                        _radioButtonTranslatorDeepl.Checked = true;
                        break;

                    default:
                        _radioButtonTranslatorYandex.Checked = true;
                        break;
                }

                bool apiUrlVisible = ActivityCommon.SelectedTranslator == ActivityCommon.TranslatorType.IbmWatson;
                _textViewApiUrlPasteTitle.Visibility = apiUrlVisible ? ViewStates.Visible : ViewStates.Gone;
                _editTextApiUrl.Visibility = apiUrlVisible ? ViewStates.Visible : ViewStates.Gone;
                _buttonApiUrlPaste.Visibility = apiUrlVisible ? ViewStates.Visible : ViewStates.Gone;

                bool pasteEnable = false;
                string clipText = _activityCommon.GetClipboardText();
                if (!string.IsNullOrWhiteSpace(clipText))
                {
                    pasteEnable = true;
                }

                _buttonYandexApiKeyPaste.Enabled = pasteEnable;

                bool testEnabled = !string.IsNullOrWhiteSpace(_editTextYandexApiKey.Text);
                switch (ActivityCommon.SelectedTranslator)
                {
                    case ActivityCommon.TranslatorType.IbmWatson:
                        if (string.IsNullOrWhiteSpace(_editTextApiUrl.Text))
                        {
                            testEnabled = false;
                        }
                        break;
                }

                _buttonYandexApiKeyTest.Enabled = testEnabled;
            }
            catch (Exception)
            {
                // ignored
            }
            finally
            {
                _ignoreChange = false;
            }
        }

        private void UpdateApiText()
        {
            if (_activityCommon == null)
            {
                return;
            }

            try
            {
                _ignoreChange = true;
                switch (ActivityCommon.SelectedTranslator)
                {
                    case ActivityCommon.TranslatorType.IbmWatson:
                        _editTextYandexApiKey.Text = ActivityCommon.IbmTranslatorApiKey;
                        _editTextApiUrl.Text = ActivityCommon.IbmTranslatorUrl;
                        break;

                    case ActivityCommon.TranslatorType.Deepl:
                        _editTextYandexApiKey.Text = ActivityCommon.DeeplApiKey;
                        _editTextApiUrl.Text = string.Empty;
                        break;

                    default:
                        _editTextYandexApiKey.Text = ActivityCommon.YandexApiKey;
                        _editTextApiUrl.Text = string.Empty;
                        break;
                }
            }
            catch (Exception)
            {
                // ignored
            }
            finally
            {
                _ignoreChange = false;
            }
        }

        private void UpdateDisplay()
        {
            UpdateApiText();
            UpdateButtonState();
        }

        private void UpdateSettings()
        {
            UpdateTranslatorKeys();
            UpdateTranslatorType();
        }

        private void UpdateTranslatorKeys()
        {
            if (_activityCommon == null)
            {
                return;
            }

            switch (ActivityCommon.SelectedTranslator)
            {
                case ActivityCommon.TranslatorType.YandexTranslate:
                    ActivityCommon.YandexApiKey = _editTextYandexApiKey.Text.Trim();
                    break;

                case ActivityCommon.TranslatorType.IbmWatson:
                    ActivityCommon.IbmTranslatorApiKey = _editTextYandexApiKey.Text.Trim();
                    ActivityCommon.IbmTranslatorUrl = _editTextApiUrl.Text.Trim();
                    break;

                case ActivityCommon.TranslatorType.Deepl:
                    ActivityCommon.DeeplApiKey = _editTextYandexApiKey.Text.Trim();
                    break;
            }
        }

        private void UpdateTranslatorType()
        {
            if (_activityCommon == null)
            {
                return;
            }

            ActivityCommon.TranslatorType translatorType = ActivityCommon.SelectedTranslator;
            if (_radioButtonTranslatorYandex.Checked)
            {
                translatorType = ActivityCommon.TranslatorType.YandexTranslate;
            }
            else if (_radioButtonTranslatorIbm.Checked)
            {
                translatorType = ActivityCommon.TranslatorType.IbmWatson;
            }
            else if (_radioButtonTranslatorDeepl.Checked)
            {
                translatorType = ActivityCommon.TranslatorType.Deepl;
            }

            _activityCommon.Translator = translatorType;
        }

        private void RestoreSetting()
        {
            if (Enum.TryParse(_instanceData.OldTranslator, out ActivityCommon.TranslatorType translator))
            {
                _activityCommon.Translator = translator;
            }

            ActivityCommon.YandexApiKey = _instanceData.OldYandexApiKey ?? string.Empty;
            ActivityCommon.IbmTranslatorApiKey = _instanceData.OldIbmTranslatorApiKey ?? string.Empty;
            ActivityCommon.IbmTranslatorUrl = _instanceData.OldIbmTranslatorUrl ?? string.Empty;
            ActivityCommon.DeeplApiKey = _instanceData.OldDeeplApiKey ?? string.Empty;
        }

        private bool SettingsChanged()
        {
            try
            {
                if (Enum.TryParse(_instanceData.OldTranslator, out ActivityCommon.TranslatorType translator))
                {
                    if (_activityCommon.Translator != translator)
                    {
                        return true;
                    }
                }

                if (string.Compare(ActivityCommon.YandexApiKey ?? string.Empty, _instanceData.OldYandexApiKey ?? string.Empty, StringComparison.Ordinal) != 0)
                {
                    return true;
                }

                if (string.Compare(ActivityCommon.IbmTranslatorApiKey ?? string.Empty, _instanceData.OldIbmTranslatorApiKey ?? string.Empty, StringComparison.Ordinal) != 0)
                {
                    return true;
                }

                if (string.Compare(ActivityCommon.IbmTranslatorUrl ?? string.Empty, _instanceData.OldIbmTranslatorUrl ?? string.Empty, StringComparison.Ordinal) != 0)
                {
                    return true;
                }

                if (string.Compare(ActivityCommon.DeeplApiKey ?? string.Empty, _instanceData.OldDeeplApiKey ?? string.Empty, StringComparison.Ordinal) != 0)
                {
                    return true;
                }

                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private bool StoreYandexKey(EventHandler handler)
        {
            UpdateSettings();
            if (!SettingsChanged())
            {
                return true;
            }

            new AlertDialog.Builder(this)
                .SetPositiveButton(Resource.String.button_yes, (sender, args) =>
                {
                    if (_activityCommon == null)
                    {
                        return;
                    }

                    SetResult(Android.App.Result.Ok);
                    handler?.Invoke(sender, args);
                })
                .SetNegativeButton(Resource.String.button_no, (sender, args) =>
                {
                    if (_activityCommon == null)
                    {
                        return;
                    }

                    RestoreSetting();
                    handler?.Invoke(sender, args);
                })
                .SetCancelable(true)
                .SetMessage(Resource.String.translate_store_key)
                .SetTitle(Resource.String.alert_title_question)
                .Show();
            return false;
        }

        private void HideKeyboard()
        {
            _imm?.HideSoftInputFromWindow(_contentView.WindowToken, HideSoftInputFlags.None);
        }
    }
}
