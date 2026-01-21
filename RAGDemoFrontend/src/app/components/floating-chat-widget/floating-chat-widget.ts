import {
  AfterViewChecked,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ElementRef,
  Inject,
  Input,
  OnDestroy,
  OnInit,
  PLATFORM_ID,
  ViewChild
} from '@angular/core';
import { CommonModule, isPlatformBrowser } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, NavigationEnd } from '@angular/router';
import { filter, Subscription } from 'rxjs';
import { HttpClient } from '@angular/common/http';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TextareaModule } from 'primeng/textarea';
import { TooltipModule } from 'primeng/tooltip';
import { TranslateModule, TranslateService } from '@ngx-translate/core';

interface ChatMessage {
  id: string;
  content: string;
  isUser: boolean;
  timestamp: Date;
  sources?: Array<{ title: string; url: string; type: string }>;
}

interface ChatRequest {
  question: string;
  sessionId?: string;
  language?: string;
}

interface ChatResponse {
  answer: string;
  sessionId?: string;
  sources?: Array<{ title: string; url: string; type: string }>;
}

@Component({
  selector: 'app-floating-chat-widget',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ButtonModule,
    CardModule,
    TextareaModule,
    ProgressSpinnerModule,
    TooltipModule,
    TranslateModule
  ],
  templateUrl: './floating-chat-widget.html',
  styleUrl: './floating-chat-widget.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FloatingChatWidget implements OnInit, AfterViewChecked, OnDestroy {
  @ViewChild('messageContainer') private messageContainer!: ElementRef;
  @Input() hideOnRoutes: string[] = ['/admin', '/login'];
  @Input() apiBase = '';

  messages: ChatMessage[] = [];
  userInput: string = '';
  isLoading: boolean = false;
  sessionId?: string;
  private shouldScrollToBottom = false;

  isOpen = false;
  isHidden = false;

  private readonly storageKey = 'floatingChatOpen';
  private routerSubscription?: Subscription;

  constructor(
    private router: Router,
    private http: HttpClient,
    private cdr: ChangeDetectorRef,
    private translate: TranslateService,
    @Inject(PLATFORM_ID) private platformId: Object
  ) {}

  ngOnInit(): void {
    if (isPlatformBrowser(this.platformId)) {
      const stored = localStorage.getItem(this.storageKey);
      this.isOpen = stored === 'true';
    }

    this.addWelcomeMessage();
    this.updateVisibility(this.router.url);
    this.routerSubscription = this.router.events
      .pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd))
      .subscribe((event) => {
        this.updateVisibility(event.urlAfterRedirects || event.url);
      });
  }

  ngAfterViewChecked(): void {
    if (this.shouldScrollToBottom) {
      this.scrollToBottom();
      this.shouldScrollToBottom = false;
    }
  }

  ngOnDestroy(): void {
    this.routerSubscription?.unsubscribe();
  }

  toggleOpen(): void {
    this.isOpen = !this.isOpen;
    this.persistState();
  }

  close(): void {
    if (this.isOpen) {
      this.isOpen = false;
      this.persistState();
    }
  }

  sendMessage(): void {
    if (!this.userInput.trim() || this.isLoading) {
      return;
    }

    const userMessage: ChatMessage = {
      id: Date.now().toString(),
      content: this.userInput,
      isUser: true,
      timestamp: new Date()
    };

    this.messages.push(userMessage);
    this.shouldScrollToBottom = true;

    const request: ChatRequest = {
      question: this.userInput,
      sessionId: this.sessionId,
      language: this.translate.getCurrentLang()
    };

    this.userInput = '';
    this.isLoading = true;

    const baseUrl = this.apiBase ? this.apiBase.replace(/\/$/, '') : '';
    const endpoint = `${baseUrl}/chat/ask`;

    this.http.post<ChatResponse>(endpoint, request).subscribe({
      next: (response) => {
        this.sessionId = response.sessionId;
        const botMessage: ChatMessage = {
          id: Date.now().toString() + '-bot',
          content: response.answer,
          isUser: false,
          timestamp: new Date(),
          sources: response.sources
        };
        this.messages.push(botMessage);
        this.shouldScrollToBottom = true;
        this.isLoading = false;
        this.cdr.markForCheck();
      },
      error: (error) => {
        console.error('Error getting response:', error);
        const errorMessage: ChatMessage = {
          id: Date.now().toString() + '-error',
          content: 'chat.errorMessage',
          isUser: false,
          timestamp: new Date()
        };
        this.messages.push(errorMessage);
        this.shouldScrollToBottom = true;
        this.isLoading = false;
        this.cdr.markForCheck();
      }
    });
  }

  onKeyPress(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      if (this.userInput.trim() && !this.isLoading) {
        this.sendMessage();
      }
    }
  }

  clearChat(): void {
    this.messages = [];
    this.sessionId = undefined;
    this.addWelcomeMessage();
  }

  formatMessage(content: string): string {
    let formatted = content;

    formatted = formatted.replace(/^### (.+)$/gm, '<h3 class="msg-heading-3">$1</h3>');
    formatted = formatted.replace(/^## (.+)$/gm, '<h2 class="msg-heading-2">$1</h2>');
    formatted = formatted.replace(/^# (.+)$/gm, '<h1 class="msg-heading-1">$1</h1>');

    formatted = formatted.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
    formatted = formatted.replace(/`([^`]+)`/g, '<code>$1</code>');

    const lines = formatted.split('\n');
    const processedLines: string[] = [];
    let inList = false;

    for (let i = 0; i < lines.length; i++) {
      const line = lines[i].trim();

      const numberedMatch = line.match(/^(\d+)\.\s+(.+)$/);
      if (numberedMatch) {
        if (!inList) {
          processedLines.push('<div class="list-container">');
          inList = true;
        }
        processedLines.push(`<div class="list-item numbered"><i class="pi pi-check-circle"></i><span>${numberedMatch[2]}</span></div>`);
        continue;
      }

      const bulletMatch = line.match(/^[\-\*]\s+(.+)$/);
      if (bulletMatch) {
        if (!inList) {
          processedLines.push('<div class="list-container">');
          inList = true;
        }
        processedLines.push(`<div class="list-item bullet"><i class="pi pi-circle-fill"></i><span>${bulletMatch[1]}</span></div>`);
        continue;
      }

      if (inList && line !== '') {
        processedLines.push('</div>');
        inList = false;
      }

      if (line !== '') {
        processedLines.push(`<div class="msg-line">${line}</div>`);
      } else {
        processedLines.push('<br>');
      }
    }

    if (inList) {
      processedLines.push('</div>');
    }

    return processedLines.join('');
  }

  private persistState(): void {
    if (isPlatformBrowser(this.platformId)) {
      localStorage.setItem(this.storageKey, String(this.isOpen));
    }
  }

  private addWelcomeMessage(): void {
    const welcomeMessage: ChatMessage = {
      id: '0',
      content: 'chat.welcomeMessage',
      isUser: false,
      timestamp: new Date()
    };
    this.messages.push(welcomeMessage);
  }

  private scrollToBottom(): void {
    try {
      const element = this.messageContainer?.nativeElement;
      if (!element) {
        return;
      }
      element.scrollTo({
        top: element.scrollHeight,
        behavior: 'smooth'
      });
    } catch (err) {
      console.error('Error scrolling to bottom:', err);
    }
  }

  private updateVisibility(url: string): void {
    const normalizedUrl = url.split('?')[0].split('#')[0];
    this.isHidden = this.hideOnRoutes.some((route) => normalizedUrl.startsWith(route));
  }
}
