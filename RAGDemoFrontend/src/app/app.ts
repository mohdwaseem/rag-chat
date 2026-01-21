import { Component, OnInit, Renderer2, Inject, PLATFORM_ID } from '@angular/core';
import { isPlatformBrowser, CommonModule } from '@angular/common';
import { RouterOutlet, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { Select } from 'primeng/select';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { FloatingChatWidget } from './components/floating-chat-widget/floating-chat-widget';
import { environment } from '../environments/environment';
import { AuthService } from './services/auth.service';

@Component({
  selector: 'app-root',
  imports: [CommonModule, RouterOutlet, RouterLink, FormsModule, ButtonModule, Select, TranslateModule, FloatingChatWidget],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App implements OnInit {
  title = 'RAG Demo Chatbot';
  apiBase = environment.apiUrl;
  currentLanguage: string = 'en';
  isAuthenticated = false;
  isAdmin = false;
  languages = [
    { label: 'English', value: 'en', code: 'EN' },
    { label: 'العربية', value: 'ar', code: 'AR' }
  ];

  constructor(
    private translate: TranslateService,
    private renderer: Renderer2,
    @Inject(PLATFORM_ID) private platformId: Object,
    private authService: AuthService
  ) {
    // Set available languages
    this.translate.addLangs(['en', 'ar']);
  }

  ngOnInit(): void {
    this.authService.authState$.subscribe(state => {
      this.isAuthenticated = state.isAuthenticated;
      this.isAdmin = state.role === 'admin';
    });

    // Load saved language preference or use default
    if (isPlatformBrowser(this.platformId)) {
      const savedLanguage = localStorage.getItem('appLanguage') || 'en';
      this.currentLanguage = savedLanguage;
      this.translate.use(savedLanguage);
      this.updateDirection(savedLanguage);
    } else {
      // Set default for SSR
      this.translate.use('en');
    }
  }

  getSelectedLanguage() {
    return this.languages.find(l => l.value === this.currentLanguage);
  }

  changeLanguage(event: any): void {
    const language = event.value;
    this.currentLanguage = language;
    this.translate.use(language);
    this.updateDirection(language);

    // Save preference
    if (isPlatformBrowser(this.platformId)) {
      localStorage.setItem('appLanguage', language);
    }
  }

  private updateDirection(language: string): void {
    const htmlElement = document.documentElement;
    if (language === 'ar') {
      this.renderer.setAttribute(htmlElement, 'dir', 'rtl');
      this.renderer.setAttribute(htmlElement, 'lang', 'ar');
    } else {
      this.renderer.setAttribute(htmlElement, 'dir', 'ltr');
      this.renderer.setAttribute(htmlElement, 'lang', 'en');
    }
  }

  logout(): void {
    this.authService.logout('/login');
  }

}

