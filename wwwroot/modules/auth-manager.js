class AuthManager {
    constructor(apiClient) {
        this.api = apiClient;
        this.user = null;
        this.isAuthenticated = false;

        this.modal = document.getElementById('authModal');
        this.form = document.getElementById('authForm');
        this.userProfile = document.getElementById('userProfile');
        this.userNameDisplay = document.getElementById('userNameDisplay');
        this.btnLogout = document.getElementById('btnLogout');
        this.errorDisplay = document.getElementById('authError');

        this.isRegisterMode = false;
    }

    async init() {
        this.bindEvents();

        this.toggleMode(false);

        await this.checkSession();

        this.checkOAuthError();
    }

    checkOAuthError() {
        const urlParams = new URLSearchParams(window.location.search);
        const error = urlParams.get('error');
        const emailConfirmed = urlParams.get('emailConfirmed');

        if (error) {
            const errorMessage = decodeURIComponent(error);
            this.showErrorDialog('Authentication Error', errorMessage);
            window.history.replaceState({}, document.title, window.location.pathname);
        }

        if (emailConfirmed === 'true') {
            this.showSuccessDialog('Email Confirmed', 'Your email has been confirmed. You can now sign in.');
            window.history.replaceState({}, document.title, window.location.pathname);
        }
    }

    bindEvents() {
        document.getElementById('tabLogin').addEventListener('click', () => this.toggleMode(false));
        document.getElementById('tabRegister').addEventListener('click', () => this.toggleMode(true));

        this.form.addEventListener('submit', async (e) => {
            e.preventDefault();
            await this.handleAuth();
        });

        if (this.btnLogout) {
            this.btnLogout.addEventListener('click', async () => await this.logout());
        }

        const btnForgotPassword = document.getElementById('btnForgotPassword');
        if (btnForgotPassword) {
            btnForgotPassword.addEventListener('click', (e) => {
                e.preventDefault();
                this.showForgotPasswordDialog();
            });
        }

        this.checkPasswordResetUrl();

        const passwordField = document.getElementById('authPassword');
        const passwordConfirmVisible = document.getElementById('authPasswordConfirmVisible');

        if (passwordField && passwordConfirmVisible) {
            const syncPasswords = () => {
                if (this.isRegisterMode && passwordField.value && !passwordConfirmVisible.value) {
                    passwordConfirmVisible.value = passwordField.value;
                }
            };

            passwordField.addEventListener('input', syncPasswords);
            passwordField.addEventListener('change', syncPasswords);

            passwordField.addEventListener('animationstart', (e) => {
                if (e.animationName === 'onAutoFillStart' || e.animationName.includes('autofill')) {
                    setTimeout(syncPasswords, 100);
                }
            });

            let lastPasswordValue = '';
            setInterval(() => {
                if (this.isRegisterMode && passwordField.value !== lastPasswordValue) {
                    lastPasswordValue = passwordField.value;
                    if (passwordField.value && !passwordConfirmVisible.value) {
                        passwordConfirmVisible.value = passwordField.value;
                    }
                }
            }, 500);
        }
    }

    toggleMode(isRegister) {
        this.isRegisterMode = isRegister;
        document.getElementById('tabLogin').classList.toggle('active', !isRegister);
        document.getElementById('tabRegister').classList.toggle('active', isRegister);

        document.getElementById('authTitle').textContent = isRegister ? 'Create Account' : 'Welcome Back';
        const authSubtitle = document.getElementById('authSubtitle');
        if (authSubtitle) {
            authSubtitle.textContent = isRegister ? 'Create a new account to get started' : 'Sign in to monitor your SQL servers';
        }
        const btnText = document.querySelector('#btnAuthAction .btn-text');
        if (btnText) btnText.textContent = isRegister ? 'Create Account' : 'Sign In';

        const forgotPasswordFormContainer = document.getElementById('forgotPasswordFormContainer');
        if (forgotPasswordFormContainer) {
            forgotPasswordFormContainer.style.display = 'none';
        }

        const authPasswordEl = document.getElementById('authPassword');
        const passwordFieldContainer = authPasswordEl ? authPasswordEl.closest('.auth-field') : null;
        const submitButton = document.getElementById('btnAuthAction');
        const authForm = document.getElementById('authForm');
        if (passwordFieldContainer) passwordFieldContainer.style.display = '';
        if (submitButton) submitButton.style.display = '';
        if (authForm) authForm.style.display = '';

        const fullNameGroup = document.getElementById('groupFullName');
        const confirmPasswordGroup = document.getElementById('groupConfirmPassword');

        if (fullNameGroup) {
            fullNameGroup.classList.toggle('hidden-field', !isRegister);
        }

        if (confirmPasswordGroup) {
            confirmPasswordGroup.classList.toggle('hidden-field', !isRegister);
        }

        const passwordField = document.getElementById('authPassword');
        const passwordConfirmVisible = document.getElementById('authPasswordConfirmVisible');

        if (passwordField) {
            passwordField.setAttribute('autocomplete', isRegister ? 'new-password' : 'current-password');
        }

        if (passwordConfirmVisible) {
            passwordConfirmVisible.value = '';
            passwordConfirmVisible.required = isRegister;
            passwordConfirmVisible.setAttribute('tabindex', isRegister ? '0' : '-1');
        }

        const forgotPasswordContainer = document.getElementById('forgotPasswordContainer');
        if (forgotPasswordContainer) {
            forgotPasswordContainer.style.display = isRegister ? 'none' : 'block';
        }

        const form = document.getElementById('authForm');
        if (form) {
            form.setAttribute('data-form-type', isRegister ? 'register' : 'login');
            form.setAttribute('action', isRegister ? '/api/auth/register' : '/api/auth/login');
        }

        this.errorDisplay.textContent = '';
        this.errorDisplay.style.display = 'none';
    }

    async checkSession() {
        try {
            const response = await fetch('/api/auth/me');
            if (response.ok) {
                this.user = await response.json();
                this.isAuthenticated = true;
                this.updateUI(true);
            } else {
                this.isAuthenticated = false;
                this.updateUI(false);
            }
        } catch (error) {
            console.error('Session check failed', error);
            this.isAuthenticated = false;
            this.updateUI(false);
        }
    }

    updateUI(isLoggedIn) {
        if (isLoggedIn) {
            this.modal.classList.remove('active');
            if (this.userProfile) this.userProfile.style.display = 'flex';
            if (this.userNameDisplay) this.userNameDisplay.textContent = this.user.fullName || this.user.email;

            if (window.MultiConnectionManager && window.MultiConnectionManager.isInitialized) {
                window.MultiConnectionManager.loadConnectionsAfterAuth();
            }

            if (window.app && typeof window.app.loadActiveConnection === 'function') {
                window.app.loadActiveConnection();
            }
        } else {
            this.modal.classList.add('active');
            if (this.userProfile) this.userProfile.style.display = 'none';
        }
    }

    async handleAuth() {
        const forgotPasswordFormContainer = document.getElementById('forgotPasswordFormContainer');
        if (forgotPasswordFormContainer && forgotPasswordFormContainer.style.display !== 'none') {
            return;
        }

        const email = document.getElementById('authEmail').value;
        const password = document.getElementById('authPassword').value;
        const fullName = document.getElementById('authName').value;
        const passwordConfirm = document.getElementById('authPasswordConfirmVisible').value;

        this.errorDisplay.textContent = '';
        this.errorDisplay.style.display = 'none';

        if (this.isRegisterMode && password !== passwordConfirm) {
            this.showErrorDialog('Passwords Do Not Match', 'Please ensure both password fields are identical.');
            return;
        }

        const btn = document.getElementById('btnAuthAction');
        const btnText = btn.querySelector('.btn-text');
        const btnLoader = btn.querySelector('.btn-loader');
        const btnArrow = btn.querySelector('.btn-arrow');

        btn.disabled = true;
        if (btnText) btnText.textContent = 'Processing...';
        if (btnLoader) btnLoader.style.display = 'block';
        if (btnArrow) btnArrow.style.display = 'none';

        try {
            let result;
            if (this.isRegisterMode) {
                result = await this.api.post('/api/auth/register', { email, password, fullName });

                if (result.requiresEmailConfirmation) {
                    this.showSuccessDialog(
                        'Registration Successful',
                        'Please check your email to confirm your account before logging in.'
                    );
                    this.toggleMode(false);
                    return;
                }
            } else {
                result = await this.api.post('/api/auth/login', { email, password });
            }

            await this.checkSession();

        } catch (error) {
            console.error('Authentication error:', error);

            if (error.requiresEmailConfirmation) {
                this.showErrorDialog(
                    'Email Confirmation Required',
                    error.message || 'Please confirm your email before logging in.',
                    true
                );
            } else if (error.details && Array.isArray(error.details)) {
                const errorList = error.details.join('\n');
                this.showErrorDialog('Registration Failed', errorList);
            } else {
                this.showErrorDialog(
                    this.isRegisterMode ? 'Registration Failed' : 'Login Failed',
                    error.message || 'Authentication failed. Please try again.'
                );
            }
        } finally {
            btn.disabled = false;
            if (btnText) btnText.textContent = this.isRegisterMode ? 'Create Account' : 'Sign In';
            if (btnLoader) btnLoader.style.display = 'none';
            if (btnArrow) btnArrow.style.display = 'block';
        }
    }

    async logout() {
        try {
            await this.api.post('/api/auth/logout', {});
            this.user = null;
            this.isAuthenticated = false;
            window.location.reload();
        } catch (error) {
            console.error('Logout failed', error);
        }
    }

    showErrorDialog(title, message, showResendLink = false) {
        this.errorDisplay.innerHTML = '';

        const titleEl = document.createElement('strong');
        titleEl.textContent = title;
        titleEl.style.display = 'block';
        titleEl.style.marginBottom = '0.5rem';

        const messageEl = document.createElement('span');
        messageEl.textContent = message;
        messageEl.style.whiteSpace = 'pre-line';

        this.errorDisplay.appendChild(titleEl);
        this.errorDisplay.appendChild(messageEl);

        if (showResendLink) {
            const resendLink = document.createElement('a');
            resendLink.href = '#';
            resendLink.textContent = 'Resend confirmation email';
            resendLink.style.display = 'block';
            resendLink.style.marginTop = '0.5rem';
            resendLink.style.color = 'var(--primary-color)';
            resendLink.onclick = async (e) => {
                e.preventDefault();
                await this.resendConfirmation();
            };
            this.errorDisplay.appendChild(resendLink);
        }

        this.errorDisplay.style.display = 'block';
    }

    showSuccessDialog(title, message) {
        this.errorDisplay.innerHTML = '';
        this.errorDisplay.style.backgroundColor = '#d4edda';
        this.errorDisplay.style.color = '#155724';
        this.errorDisplay.style.borderColor = '#c3e6cb';

        const titleEl = document.createElement('strong');
        titleEl.textContent = title;
        titleEl.style.display = 'block';
        titleEl.style.marginBottom = '0.5rem';

        const messageEl = document.createElement('span');
        messageEl.textContent = message;

        this.errorDisplay.appendChild(titleEl);
        this.errorDisplay.appendChild(messageEl);
        this.errorDisplay.style.display = 'block';

        setTimeout(() => {
            this.errorDisplay.style.backgroundColor = '';
            this.errorDisplay.style.color = '';
            this.errorDisplay.style.borderColor = '';
        }, 5000);
    }

    async resendConfirmation() {
        const email = document.getElementById('authEmail').value;
        if (!email) {
            this.showErrorDialog('Error', 'Please enter your email address.');
            return;
        }

        try {
            await this.api.post('/api/auth/resend-confirmation', { email });
            this.showSuccessDialog('Email Sent', 'If an account exists, a confirmation email has been sent.');
        } catch (error) {
            this.showErrorDialog('Error', error.message || 'Failed to resend confirmation email.');
        }
    }

    showForgotPasswordDialog() {
        document.getElementById('authTitle').textContent = 'Forgot Password?';
        const authSubtitle = document.getElementById('authSubtitle');
        if (authSubtitle) {
            authSubtitle.textContent = 'Enter your email to reset your password';
        }

        const tabsWrapper = document.querySelector('.auth-tabs-wrapper');
        if (tabsWrapper) tabsWrapper.style.display = 'none';

        const emailField = document.getElementById('authEmail')?.closest('.auth-field');
        const passwordField = document.getElementById('authPassword')?.closest('.auth-field');
        const confirmPasswordField = document.getElementById('groupConfirmPassword');
        const fullNameField = document.getElementById('groupFullName');
        const forgotPasswordLink = document.getElementById('forgotPasswordContainer');
        const submitButton = document.getElementById('btnAuthAction');
        const authForm = document.getElementById('authForm');

        if (emailField) emailField.style.display = 'none';
        if (passwordField) passwordField.style.display = 'none';
        if (confirmPasswordField) confirmPasswordField.style.display = 'none';
        if (fullNameField) fullNameField.style.display = 'none';
        if (forgotPasswordLink) forgotPasswordLink.style.display = 'none';
        if (submitButton) submitButton.style.display = 'none';
        if (authForm) authForm.style.display = 'none';

        const forgotPasswordFormContainer = document.getElementById('forgotPasswordFormContainer');
        if (forgotPasswordFormContainer) {
            forgotPasswordFormContainer.style.display = 'block';
        }

        const emailInput = document.getElementById('forgotPasswordEmail');
        const authEmail = document.getElementById('authEmail');
        if (emailInput && authEmail && authEmail.value) {
            emailInput.value = authEmail.value;
        }

        setTimeout(() => {
            if (emailInput) emailInput.focus();
        }, 100);

        const cancelBtn = document.getElementById('btnCancelForgotPassword');
        const submitBtn = document.getElementById('btnSubmitForgotPassword');

        if (cancelBtn && !cancelBtn.dataset.bound) {
            cancelBtn.dataset.bound = 'true';
            cancelBtn.addEventListener('click', () => this.hideForgotPasswordForm());
        }

        if (submitBtn && !submitBtn.dataset.bound) {
            submitBtn.dataset.bound = 'true';
            submitBtn.addEventListener('click', async () => {
                await this.handleForgotPasswordSubmit();
            });
        }
    }

    hideForgotPasswordForm() {
        document.getElementById('authTitle').textContent = 'Welcome Back';
        const authSubtitle = document.getElementById('authSubtitle');
        if (authSubtitle) {
            authSubtitle.textContent = 'Sign in to monitor your SQL servers';
        }

        const tabsWrapper = document.querySelector('.auth-tabs-wrapper');
        if (tabsWrapper) tabsWrapper.style.display = '';

        const emailField = document.getElementById('authEmail')?.closest('.auth-field');
        const passwordField = document.getElementById('authPassword')?.closest('.auth-field');
        const confirmPasswordField = document.getElementById('groupConfirmPassword');
        const fullNameField = document.getElementById('groupFullName');
        const forgotPasswordLink = document.getElementById('forgotPasswordContainer');
        const submitButton = document.getElementById('btnAuthAction');
        const authForm = document.getElementById('authForm');

        if (emailField) emailField.style.display = '';
        if (passwordField) passwordField.style.display = '';
        if (confirmPasswordField) {
            confirmPasswordField.style.display = this.isRegisterMode ? '' : 'none';
        }
        if (fullNameField) {
            fullNameField.style.display = this.isRegisterMode ? '' : 'none';
        }
        if (forgotPasswordLink && !this.isRegisterMode) forgotPasswordLink.style.display = 'block';
        if (submitButton) submitButton.style.display = '';
        if (authForm) authForm.style.display = '';

        const forgotPasswordFormContainer = document.getElementById('forgotPasswordFormContainer');
        if (forgotPasswordFormContainer) {
            forgotPasswordFormContainer.style.display = 'none';
        }

        this.errorDisplay.textContent = '';
        this.errorDisplay.style.display = 'none';
    }

    async handleForgotPasswordSubmit() {
        const emailInput = document.getElementById('forgotPasswordEmail');
        const submitBtn = document.getElementById('btnSubmitForgotPassword');
        const btnText = submitBtn?.querySelector('.btn-text');
        const btnLoader = submitBtn?.querySelector('.btn-loader');

        const email = emailInput?.value.trim();

        if (!email) {
            this.showErrorDialog('Error', 'Please enter your email address.');
            return;
        }

        const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
        if (!emailRegex.test(email)) {
            this.showErrorDialog('Error', 'Please enter a valid email address.');
            return;
        }

        if (submitBtn) submitBtn.disabled = true;
        if (btnText) btnText.textContent = 'Sending...';
        if (btnLoader) btnLoader.style.display = 'block';

        try {
            await this.api.post('/api/auth/forgot-password', { email });

            this.showSuccessDialog(
                'Password Reset Email Sent',
                'If an account exists, a password reset email has been sent. Please check your inbox.'
            );

            setTimeout(() => {
                this.hideForgotPasswordForm();
            }, 2000);
        } catch (error) {
            this.showErrorDialog('Error', error.message || 'Failed to send password reset email. Please try again.');
        } finally {
            if (submitBtn) submitBtn.disabled = false;
            if (btnText) btnText.textContent = 'Send Reset Link';
            if (btnLoader) btnLoader.style.display = 'none';
        }
    }

    formatExpiryMinutes(minutes) {
        const m = Number(minutes);
        if (!Number.isFinite(m) || m <= 0) return 'a few minutes';

        if (m < 60) {
            return m === 1 ? '1 minute' : `${m} minutes`;
        }

        if (m < 60 * 24) {
            const hours = Math.floor(m / 60);
            const remainingMinutes = m % 60;

            if (remainingMinutes === 0) {
                return hours === 1 ? '1 hour' : `${hours} hours`;
            }

            const hourText = hours === 1 ? '1 hour' : `${hours} hours`;
            const minuteText = remainingMinutes === 1 ? '1 minute' : `${remainingMinutes} minutes`;
            return `${hourText} ${minuteText}`;
        }

        const days = Math.floor(m / (60 * 24));
        const remainingHours = Math.floor((m % (60 * 24)) / 60);

        if (remainingHours === 0) {
            return days === 1 ? '1 day' : `${days} days`;
        }

        const dayText = days === 1 ? '1 day' : `${days} days`;
        const hourText2 = remainingHours === 1 ? '1 hour' : `${remainingHours} hours`;
        return `${dayText} ${hourText2}`;
    }

    async sendPasswordResetEmail(email) {
        try {
            const response = await this.api.post('/api/auth/forgot-password', { email });
            const expiresMinutes = response?.expiresMinutes;
            const expiryText = this.formatExpiryMinutes(expiresMinutes ?? 5);
            this.showSuccessDialog(
                'Password Reset Email Sent',
                `If an account exists, a password reset email has been sent. The link expires in ${expiryText}.`
            );
        } catch (error) {
            this.showErrorDialog('Error', error.message || 'Failed to send password reset email.');
        }
    }

    checkPasswordResetUrl() {
        const urlParams = new URLSearchParams(window.location.search);
        if (urlParams.get('resetPassword') === 'true') {
            const userId = urlParams.get('userId');
            const token = urlParams.get('token');
            const expiresMinutes = urlParams.get('expiresMinutes');

            if (userId && token) {
                setTimeout(() => this.showResetPasswordForm(userId, token, expiresMinutes), 500);
                window.history.replaceState({}, document.title, window.location.pathname);
            }
        }
    }

    async showResetPasswordForm(userId, token, expiresMinutes) {
        let userEmail = '';
        try {
            const user = await this.api.get(`/api/auth/user/${userId}`);
            if (user && user.email) {
                userEmail = user.email;
            }
        } catch (error) {

        }

        const expiryText = this.formatExpiryMinutes(expiresMinutes ?? 5);

        const html = `
            <div style="position: fixed; top: 0; left: 0; right: 0; bottom: 0; background: rgba(0,0,0,0.5); display: flex; align-items: center; justify-content: center; z-index: 10000;">
                <div style="background: white; border-radius: 12px; padding: 32px; max-width: 400px; width: 90%; box-shadow: 0 20px 60px rgba(0,0,0,0.3);">
                    <h2 style="margin: 0 0 24px 0; color: #1f2937; font-size: 24px;">Reset Password</h2>
                    <p style="color: #6b7280; font-size: 13px; margin: -12px 0 16px 0;">This reset link is valid for ${expiryText}.</p>

                    ${userEmail ? `<p style="color: #6b7280; font-size: 14px; margin-bottom: 20px;">Resetting password for: <strong style="color: #1f2937;">${userEmail.replace(/[&<>"']/g, (m) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[m]))}</strong></p>` : '<p style="color: #6b7280; font-size: 14px; margin-bottom: 20px;">Enter your new password below.</p>'}

                    <form id="resetPasswordForm" style="display: flex; flex-direction: column; gap: 16px;">
                        <div>
                            <label style="display: block; margin-bottom: 8px; color: #374151; font-weight: 500;">New Password</label>
                            <input type="password" id="resetPassword" placeholder="Enter new password" required autocomplete="new-password" style="width: 100%; padding: 10px; border: 1px solid #d1d5db; border-radius: 8px; font-size: 16px; box-sizing: border-box;">
                        </div>

                        <div>
                            <label style="display: block; margin-bottom: 8px; color: #374151; font-weight: 500;">Confirm Password</label>
                            <input type="password" id="resetPasswordConfirm" placeholder="Confirm new password" required autocomplete="new-password" style="width: 100%; padding: 10px; border: 1px solid #d1d5db; border-radius: 8px; font-size: 16px; box-sizing: border-box;">
                        </div>

                        <div id="resetError" style="display: none; padding: 12px; background: #fee2e2; color: #991b1b; border-radius: 8px; font-size: 14px;"></div>

                        <div style="display: flex; gap: 12px;">
                            <button type="button" id="btnCancelReset" style="flex: 1; padding: 10px; border: 1px solid #d1d5db; border-radius: 8px; background: white; color: #374151; cursor: pointer; font-weight: 500;">Cancel</button>
                            <button type="submit" id="btnSubmitReset" style="flex: 1; padding: 10px; border: none; border-radius: 8px; background: #6366f1; color: white; cursor: pointer; font-weight: 500;">Reset Password</button>
                        </div>
                    </form>
                </div>
            </div>
        `;

        const container = document.createElement('div');
        container.innerHTML = html;
        container.id = 'resetPasswordDialog';
        document.body.appendChild(container);

        const form = document.getElementById('resetPasswordForm');
        const cancelBtn = document.getElementById('btnCancelReset');
        const errorDiv = document.getElementById('resetError');

        cancelBtn.addEventListener('click', () => container.remove());

        form.addEventListener('submit', async (e) => {
            e.preventDefault();

            const password = document.getElementById('resetPassword').value;
            const passwordConfirm = document.getElementById('resetPasswordConfirm').value;

            if (password !== passwordConfirm) {
                errorDiv.textContent = 'Passwords do not match.';
                errorDiv.style.display = 'block';
                return;
            }

            if (password.length < 6) {
                errorDiv.textContent = 'Password must be at least 6 characters.';
                errorDiv.style.display = 'block';
                return;
            }

            try {
                await this.api.post('/api/auth/reset-password', {
                    userId,
                    token,
                    newPassword: password
                });

                container.remove();
                this.showSuccessDialog(
                    'Password Reset Successful',
                    'Your password has been reset. Switching to login form - please sign in with your new password.'
                );
                setTimeout(() => this.toggleMode(false), 2000);
            } catch (error) {
                const errorMessage = error.message || 'Failed to reset password. The link may have expired.';
                errorDiv.textContent = errorMessage;
                errorDiv.style.display = 'block';

                if (errorMessage.toLowerCase().includes('expired') || errorMessage.toLowerCase().includes('expire')) {
                    setTimeout(() => {
                        container.remove();
                        this.hideForgotPasswordForm();
                        this.toggleMode(false);
                        this.showSuccessDialog('Redirected to Login', 'The reset password link has expired. Please request a new password reset.');
                    }, 60000);
                }
            }
        });
    }
}
