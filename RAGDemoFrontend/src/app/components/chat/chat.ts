import { Component, OnInit, ViewChild, ElementRef, AfterViewChecked, ChangeDetectorRef, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ButtonModule } from 'primeng/button';
import { CardModule } from 'primeng/card';
import { ProgressSpinnerModule } from 'primeng/progressspinner';
import { TooltipModule } from 'primeng/tooltip';
import { TextareaModule } from 'primeng/textarea';
import { ChatService } from '../../services/chat.service';
import { ChatMessage, ChatRequest } from '../../models/chat.model';
import { TranslateModule, TranslateService } from '@ngx-translate/core';

@Component({
  selector: 'app-chat',
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
  templateUrl: './chat.html',
  styleUrl: './chat.scss',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class Chat implements OnInit, AfterViewChecked {
  @ViewChild('messageContainer') private messageContainer!: ElementRef;

  messages: ChatMessage[] = [];
  userInput: string = '';
  isLoading: boolean = false;
  sessionId?: string;
  private shouldScrollToBottom = false;

  constructor(
    private chatService: ChatService,
    private cdr: ChangeDetectorRef,
    private translate: TranslateService
  ) {}

  ngOnInit(): void {
    this.addWelcomeMessage();
  }

  ngAfterViewChecked(): void {
    if (this.shouldScrollToBottom) {
      this.scrollToBottom();
      this.shouldScrollToBottom = false;
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

    this.chatService.askQuestion(request).subscribe({
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

  private scrollToBottom(): void {
    try {
      const element = this.messageContainer.nativeElement;
      element.scrollTo({
        top: element.scrollHeight,
        behavior: 'smooth'
      });
    } catch (err) {
      console.error('Error scrolling to bottom:', err);
    }
  }

  formatMessage(content: string): string {
    let formatted = content;

    // Format headings (###, ##, #)
    formatted = formatted.replace(/^### (.+)$/gm, '<h3 class="msg-heading-3">$1</h3>');
    formatted = formatted.replace(/^## (.+)$/gm, '<h2 class="msg-heading-2">$1</h2>');
    formatted = formatted.replace(/^# (.+)$/gm, '<h1 class="msg-heading-1">$1</h1>');

    // Format bold text (**text**)
    formatted = formatted.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');

    // Format inline code (`code`)
    formatted = formatted.replace(/`([^`]+)`/g, '<code>$1</code>');

    // Split into lines for list processing
    const lines = formatted.split('\n');
    const processedLines: string[] = [];
    let inList = false;

    for (let i = 0; i < lines.length; i++) {
      const line = lines[i].trim();

      // Check for numbered list items
      const numberedMatch = line.match(/^(\d+)\.\s+(.+)$/);
      if (numberedMatch) {
        if (!inList) {
          processedLines.push('<div class="list-container">');
          inList = true;
        }
        processedLines.push(`<div class="list-item numbered"><i class="pi pi-check-circle"></i><span>${numberedMatch[2]}</span></div>`);
        continue;
      }

      // Check for bullet list items (-, *)
      const bulletMatch = line.match(/^[\-\*]\s+(.+)$/);
      if (bulletMatch) {
        if (!inList) {
          processedLines.push('<div class="list-container">');
          inList = true;
        }
        processedLines.push(`<div class="list-item bullet"><i class="pi pi-circle-fill"></i><span>${bulletMatch[1]}</span></div>`);
        continue;
      }

      // Close list if we were in one
      if (inList && line !== '') {
        processedLines.push('</div>');
        inList = false;
      }

      // Add regular line
      if (line !== '') {
        processedLines.push(`<div class="msg-line">${line}</div>`);
      } else {
        processedLines.push('<br>');
      }
    }

    // Close list if still open
    if (inList) {
      processedLines.push('</div>');
    }

    return processedLines.join('');
  }
}
