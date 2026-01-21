import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { Select } from 'primeng/select';
import { AuthService } from '../../../services/auth.service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    CardModule,
    ButtonModule,
    InputTextModule,
    Select,
    TranslateModule
  ],
  templateUrl: './register.html',
  styleUrl: './register.scss'
})
export class Register implements OnInit {
  username = '';
  password = '';
  confirmPassword = '';
  errorMessage = '';
  isLoading = false;
  isAdmin = false;
  selectedRole: 'user' | 'admin' = 'user';
  roleOptions: Array<{ label: string; value: 'user' | 'admin' }> = [];

  constructor(
    private authService: AuthService,
    private router: Router,
    private translate: TranslateService,
    private cdr: ChangeDetectorRef
  ) {
    this.authService.authState$.subscribe(state => {
      this.isAdmin = state.role === 'admin';
    });

    this.translate.onLangChange.subscribe(() => {
      this.updateRoleOptions();
    });
  }

  ngOnInit(): void {
    this.updateRoleOptions();
  }

  submit(): void {
    this.errorMessage = '';

    if (this.password !== this.confirmPassword) {
      this.setErrorMessage(this.translate.instant('auth.passwordMismatch'));
      return;
    }

    this.isLoading = true;

    const role = this.isAdmin ? this.selectedRole : 'user';

    this.authService.register({ username: this.username, password: this.password, role }).subscribe({
      next: () => {
        this.isLoading = false;
        this.router.navigate(['/login']);
      },
      error: (error) => {
        this.isLoading = false;
        this.setErrorMessage(error?.error?.message || 'Registration failed');
      }
    });
  }

  private updateRoleOptions(): void {
    this.roleOptions = [
      { label: this.translate.instant('auth.roles.user'), value: 'user' },
      { label: this.translate.instant('auth.roles.admin'), value: 'admin' }
    ];
  }

  private setErrorMessage(message: string): void {
    queueMicrotask(() => {
      this.errorMessage = message;
      this.cdr.detectChanges();
    });
  }
}
